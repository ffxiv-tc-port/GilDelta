using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using GilDelta.Wallet;

namespace GilDelta.Events;

/// <summary>
/// Append-only JSONL event log. One file per character, keyed by the path.
/// The schema version stays at 1 on purpose even though the <c>cid</c> field was
/// added later: <c>cid</c> is an <em>optional</em> field, older builds of the plugin
/// ignore unknown properties, and <see cref="LoadAll"/> here rejects any version it
/// does not know. Bumping the version would make every row written by the new build
/// silently invisible to an older one.
/// </summary>
public sealed class EventStore
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    // Taken from the serializer itself, so the fast reader accepts exactly the text Append writes.
    private static readonly (byte[] Name, WalletKind Value)[] KindNames = EnumNames<WalletKind>();
    private static readonly (byte[] Name, GilEventCategory Value)[] CategoryNames = EnumNames<GilEventCategory>();

    private readonly string _path;
    private readonly ulong _owner;

    /// <param name="path">JSONL file backing this character's log.</param>
    /// <param name="ownerContentId">
    /// The character this file belongs to. Rows written before the <c>cid</c> field
    /// existed are attributed to it on load — the file name is a recorded owner, not
    /// a guess about who happens to be logged in now. 0 keeps them in the
    /// "unknown character" bucket.
    /// </param>
    public EventStore(string path, ulong ownerContentId = 0)
    {
        _path = path;
        _owner = ownerContentId;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>The character this file belongs to; 0 = unknown.</summary>
    public ulong OwnerContentId => _owner;

    public void Append(GilEvent ev)
    {
        var record = StoredEvent.From(ev, CurrentSchemaVersion, _owner);
        var line = JsonSerializer.Serialize(record, JsonOpts);
        File.AppendAllText(_path, line + "\n");
    }

    /// <summary>
    /// Renders a whole file's worth of events, in the exact format <see cref="Append"/>
    /// produces, without touching the disk. The caller writes the result out itself -
    /// that is what keeps the rewrite off the framework thread: serialize here, hand
    /// the string to a background writer.
    /// </summary>
    public string SerializeAll(IEnumerable<GilEvent> events)
    {
        var sb = new StringBuilder();
        foreach (var ev in events)
        {
            var record = StoredEvent.From(ev, CurrentSchemaVersion, _owner);
            sb.Append(JsonSerializer.Serialize(record, JsonOpts));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <remarks>
    /// Reads the file as UTF-8 bytes in pooled chunks: no string per line and no serializer
    /// state per record. Every line the fast reader is not certain about goes through
    /// <see cref="TryReadText"/>, which is the original per-line logic, so that path
    /// remains the authority on what a line means.
    /// </remarks>
    public IEnumerable<GilEvent> LoadAll()
    {
        if (!File.Exists(_path)) yield break;

        using var source = new LineSource(_path);
        if (source.HasNonUtf8Bom)
        {
            foreach (var ev in LoadAllAsText()) yield return ev;
            yield break;
        }

        while (source.TryNextLine(out var start, out var length))
        {
            if (TryReadLine(source.Buffer.AsSpan(start, length), out var ev)) yield return ev;
        }
    }

    // A file re-saved as UTF-16/32 by an editor: StreamReader decodes those, so let it.
    private IEnumerable<GilEvent> LoadAllAsText()
    {
        foreach (var line in File.ReadLines(_path))
        {
            if (TryReadText(line, out var ev)) yield return ev;
        }
    }

    private bool TryReadLine(ReadOnlySpan<byte> line, out GilEvent ev)
    {
        // Invalid UTF-8 goes through the decoder StreamReader used, U+FFFD replacement included.
        if (Utf8.IsValid(line) && TryReadFast(line, out ev)) return true;
        return TryReadText(Encoding.UTF8.GetString(line), out ev);
    }

    private bool TryReadText(string line, out GilEvent ev)
    {
        ev = null!;
        if (string.IsNullOrWhiteSpace(line)) return false;

        StoredEvent? record;
        try
        {
            record = JsonSerializer.Deserialize<StoredEvent>(line, JsonOpts);
        }
        catch
        {
            return false;  // production logs Warning here
        }

        if (record is null) return false;
        if (record.V != CurrentSchemaVersion) return false;

        ev = record.ToDomain(_owner);
        return true;
    }

    /// <summary>
    /// Accepts only the exact shape <see cref="Append"/> writes: every field at most once,
    /// v/ts/wallet/amount/category present, no unknown fields, enum names spelt as the
    /// serializer spells them. Anything else returns false and is left to the serializer.
    /// </summary>
    private bool TryReadFast(ReadOnlySpan<byte> line, out GilEvent ev)
    {
        ev = null!;
        var reader = new Utf8JsonReader(line);
        int v = 0;
        DateTimeOffset ts = default;
        long amount = 0;
        WalletKind kind = default;
        string? id = null, note = null;
        GilEventCategory category = default;
        ulong? cid = null;
        bool? suspect = null;
        var seen = 0;

        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                int bit;
                if (reader.ValueTextEquals("v"u8))
                {
                    bit = 1;
                    if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out v)) return false;
                }
                else if (reader.ValueTextEquals("ts"u8))
                {
                    bit = 2;
                    if (!reader.Read() || reader.TokenType != JsonTokenType.String || !reader.TryGetDateTimeOffset(out ts)) return false;
                }
                else if (reader.ValueTextEquals("wallet"u8))
                {
                    bit = 4;
                    if (!reader.Read() || !TryReadWallet(ref reader, out kind, out id)) return false;
                }
                else if (reader.ValueTextEquals("amount"u8))
                {
                    bit = 8;
                    if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out amount)) return false;
                }
                else if (reader.ValueTextEquals("category"u8))
                {
                    bit = 16;
                    if (!reader.Read() || !TryMatch(ref reader, CategoryNames, out category)) return false;
                }
                else if (reader.ValueTextEquals("note"u8))
                {
                    bit = 32;
                    if (!reader.Read()) return false;
                    if (reader.TokenType == JsonTokenType.String) note = reader.GetString();
                    else if (reader.TokenType != JsonTokenType.Null) return false;
                }
                else if (reader.ValueTextEquals("cid"u8))
                {
                    bit = 64;
                    if (!reader.Read()) return false;
                    if (reader.TokenType == JsonTokenType.Number)
                    {
                        if (!reader.TryGetUInt64(out var c)) return false;
                        cid = c;
                    }
                    else if (reader.TokenType != JsonTokenType.Null) return false;
                }
                else if (reader.ValueTextEquals("suspect"u8))
                {
                    bit = 128;
                    if (!reader.Read()) return false;
                    if (reader.TokenType is JsonTokenType.True or JsonTokenType.False) suspect = reader.GetBoolean();
                    else if (reader.TokenType != JsonTokenType.Null) return false;
                }
                else
                {
                    return false;
                }

                if ((seen & bit) != 0) return false;
                seen |= bit;
            }

            if (reader.TokenType != JsonTokenType.EndObject || reader.CurrentDepth != 0 || reader.Read()) return false;
        }
        catch
        {
            // Not only JsonException: GetString throws InvalidOperationException on a lone
            // surrogate escape. Whatever went wrong, the serializer gets the final word.
            return false;
        }

        if ((seen & 31) != 31 || v != CurrentSchemaVersion) return false;

        ev = Build(ts, kind, id!, amount, category, note, cid, suspect, _owner);
        return true;
    }

    private static bool TryReadWallet(ref Utf8JsonReader reader, out WalletKind kind, out string? id)
    {
        kind = default;
        id = null;
        if (reader.TokenType != JsonTokenType.StartObject) return false;

        var seen = 0;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("kind"u8))
            {
                if ((seen & 1) != 0 || !reader.Read() || !TryMatch(ref reader, KindNames, out kind)) return false;
                seen |= 1;
            }
            else if (reader.ValueTextEquals("id"u8))
            {
                if ((seen & 2) != 0 || !reader.Read() || reader.TokenType != JsonTokenType.String) return false;
                id = reader.GetString();
                seen |= 2;
            }
            else
            {
                return false;
            }
        }

        return seen == 3 && reader.TokenType == JsonTokenType.EndObject;
    }

    private static bool TryMatch<T>(ref Utf8JsonReader reader, (byte[] Name, T Value)[] names, out T value)
        where T : struct
    {
        value = default;
        if (reader.TokenType != JsonTokenType.String) return false;
        foreach (var (name, candidate) in names)
        {
            if (!reader.ValueTextEquals(name)) continue;
            value = candidate;
            return true;
        }
        return false;
    }

    private static (byte[] Name, T Value)[] EnumNames<T>() where T : struct, Enum
    {
        var list = new List<(byte[], T)>();
        foreach (var value in Enum.GetValues<T>())
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(value, JsonOpts);
            if (json.Length >= 2 && json[0] == (byte)'"' && json[^1] == (byte)'"' && Array.IndexOf(json, (byte)'\\') < 0)
                list.Add((json[1..^1], value));
        }
        return list.ToArray();
    }

    private static GilEvent Build(DateTimeOffset ts, WalletKind kind, string id, long amount,
        GilEventCategory category, string? note, ulong? cid, bool? suspect, ulong owner) =>
        new(ts, new WalletId(kind, id), amount, category, note)
        {
            ContentId = cid ?? owner,
            Suspect = suspect ?? false,
        };

    /// <summary>
    /// Hands out one line at a time from a pooled buffer, split exactly where
    /// StreamReader.ReadLine splits (\r, \n, \r\n). Empty lines are not returned.
    /// The span a caller gets is only valid until the next call.
    /// </summary>
    private sealed class LineSource : IDisposable
    {
        private const int ChunkSize = 64 * 1024;

        private readonly FileStream _stream;
        private byte[] _buffer;
        private int _start;
        private int _end;
        private bool _eof;

        public LineSource(string path)
        {
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);
            _buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
            while (!_eof && _end < 4) Fill();

            var head = _buffer.AsSpan(0, _end);
            if (head.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) _start = 3;
            HasNonUtf8Bom = head.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])
                         || head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])
                         || head.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]);
        }

        public bool HasNonUtf8Bom { get; }

        public byte[] Buffer => _buffer;

        public bool TryNextLine(out int start, out int length)
        {
            while (true)
            {
                var idx = _buffer.AsSpan(_start, _end - _start).IndexOfAny((byte)'\r', (byte)'\n');
                if (idx >= 0)
                {
                    start = _start;
                    length = idx;
                    _start += idx + 1;
                    if (length > 0) return true;
                    continue;
                }

                if (_eof)
                {
                    start = _start;
                    length = _end - _start;
                    _start = _end;
                    return length > 0;
                }

                Fill();
            }
        }

        private void Fill()
        {
            if (_start > 0)
            {
                System.Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
                _end -= _start;
                _start = 0;
            }

            if (_end == _buffer.Length)
            {
                var bigger = ArrayPool<byte>.Shared.Rent(_buffer.Length * 2);
                System.Buffer.BlockCopy(_buffer, 0, bigger, 0, _end);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = bigger;
            }

            var read = _stream.Read(_buffer, _end, _buffer.Length - _end);
            if (read == 0) _eof = true;
            else _end += read;
        }

        public void Dispose()
        {
            _stream.Dispose();
            ArrayPool<byte>.Shared.Return(_buffer);
        }
    }

    /// <summary>
    /// Re-runs <paramref name="inferrer"/> over events in [<paramref name="from"/>, <paramref name="to"/>],
    /// replacing their category and note. Out-of-range events are preserved verbatim.
    /// The previous file is preserved as <c>&lt;path&gt;.bak</c>.
    /// </summary>
    public void Reclassify(DateTimeOffset from, DateTimeOffset to, Inferrer inferrer)
    {
        if (!File.Exists(_path)) return;

        var current = LoadAll().ToList();
        var rewritten = new List<GilEvent>(current.Count);

        foreach (var ev in current)
        {
            if (ev.Timestamp < from || ev.Timestamp > to)
            {
                rewritten.Add(ev);
                continue;
            }

            var diff = new WalletDiff(ev.Wallet, 0, ev.Amount, ev.Timestamp);
            var ctx = GameContext.Empty(ev.Timestamp);
            var reclassified = inferrer.Classify(diff, ctx);
            // Both of these have to be carried across by hand: the rules build a fresh
            // event and know about neither the owner nor the user's own exclusion.
            rewritten.Add(reclassified with { ContentId = ev.ContentId, Suspect = ev.Suspect });
        }

        var bakPath = _path + ".bak";
        File.Copy(_path, bakPath, overwrite: true);

        var temp = _path + ".tmp";
        using (var writer = new StreamWriter(temp, append: false))
        {
            foreach (var ev in rewritten)
            {
                var record = StoredEvent.From(ev, CurrentSchemaVersion, _owner);
                writer.WriteLine(JsonSerializer.Serialize(record, JsonOpts));
            }
        }
        File.Replace(temp, _path, destinationBackupFileName: null);
    }

    /// <summary>Internal DTO for JSON (de)serialization.</summary>
    private sealed record StoredEvent(
        [property: JsonPropertyName("v")]        int V,
        [property: JsonPropertyName("ts")]       DateTimeOffset Ts,
        [property: JsonPropertyName("wallet")]   StoredWallet Wallet,
        [property: JsonPropertyName("amount")]   long Amount,
        [property: JsonPropertyName("category")] GilEventCategory Category,
        [property: JsonPropertyName("note")]     string? Note,
        // Nullable so JsonIgnoreCondition.WhenWritingNull leaves it out entirely for
        // an unknown owner, keeping rows byte-identical to what v1 produced.
        [property: JsonPropertyName("cid")]      ulong? Cid = null,
        // Same trick, same reason: written only when the user has actually flagged the
        // row, so an untouched log stays byte-for-byte what it was.
        [property: JsonPropertyName("suspect")]  bool? Suspect = null)
    {
        public static StoredEvent From(GilEvent e, int version, ulong owner)
        {
            var cid = e.ContentId != 0 ? e.ContentId : owner;
            return new StoredEvent(version, e.Timestamp,
                new StoredWallet(e.Wallet.Kind, e.Wallet.Identifier),
                e.Amount, e.Category, e.Note,
                cid != 0 ? cid : null,
                e.Suspect ? true : null);
        }

        public GilEvent ToDomain(ulong owner) =>
            Build(Ts, Wallet.Kind, Wallet.Id, Amount, Category, Note, Cid, Suspect, owner);
    }

    private sealed record StoredWallet(
        [property: JsonPropertyName("kind")] WalletKind Kind,
        [property: JsonPropertyName("id")]   string Id);
}
