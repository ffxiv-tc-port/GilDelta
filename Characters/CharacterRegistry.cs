using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GilDelta.Characters;

/// <summary>
/// Roster of every character on the account that has produced gil records.
/// Mutated only from the framework thread. Persistence never happens in here —
/// callers take <see cref="Serialize"/> on the framework thread and hand the
/// already-serialized string to a background writer, so no file I/O ever runs
/// while the roster is being walked.
/// </summary>
public sealed class CharacterRegistry
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Dictionary<ulong, CharacterInfo> _map = new();

    public int Count => _map.Count;

    /// <summary>Every known character, lowest content id first. Excludes the unknown bucket.</summary>
    public IReadOnlyList<CharacterInfo> All =>
        _map.Values.OrderBy(c => c.ContentId).ToList();

    /// <summary>
    /// Returns what we know about a content id. Unknown ids get a placeholder rather
    /// than null so callers never have to invent a label.
    /// </summary>
    public CharacterInfo Get(ulong contentId) =>
        _map.TryGetValue(contentId, out var info)
            ? info
            : contentId == CharacterInfo.UnknownContentId
                ? CharacterInfo.Unknown
                : new CharacterInfo(contentId, string.Empty, string.Empty);

    public bool Contains(ulong contentId) => _map.ContainsKey(contentId);

    /// <summary>
    /// Records or refines a character. An empty/null name or world never overwrites a
    /// value we already have — right after login the game can hand back a content id
    /// several frames before the name and home world are populated.
    /// </summary>
    /// <returns>true when the roster actually changed (i.e. it needs persisting).</returns>
    public bool Observe(ulong contentId, string? name, string? world)
    {
        if (contentId == CharacterInfo.UnknownContentId) return false;

        var n = name ?? string.Empty;
        var w = world ?? string.Empty;

        if (_map.TryGetValue(contentId, out var current))
        {
            var merged = current with
            {
                Name  = n.Length > 0 ? n : current.Name,
                World = w.Length > 0 ? w : current.World,
            };
            if (merged == current) return false;
            _map[contentId] = merged;
            return true;
        }

        _map[contentId] = new CharacterInfo(contentId, n, w);
        return true;
    }

    /// <summary>
    /// Makes sure a content id has a roster row even when nothing is known about it
    /// (e.g. it was discovered by finding an <c>events/&lt;id&gt;.jsonl</c> file).
    /// </summary>
    public bool EnsureKnown(ulong contentId) => Observe(contentId, null, null);

    public string Serialize() =>
        JsonSerializer.Serialize(
            _map.Values.OrderBy(c => c.ContentId)
                .Select(c => new Row(c.ContentId, c.Name, c.World))
                .ToList(),
            JsonOpts);

    /// <summary>
    /// Merges a persisted roster in. Malformed JSON is ignored rather than thrown:
    /// a corrupt roster must never stop the plugin from loading the event log.
    /// </summary>
    public void LoadFrom(string json)
    {
        List<Row>? rows;
        try
        {
            rows = JsonSerializer.Deserialize<List<Row>>(json, JsonOpts);
        }
        catch
        {
            return;
        }

        if (rows is null) return;

        foreach (var row in rows)
        {
            if (row.Cid == CharacterInfo.UnknownContentId) continue;
            _map[row.Cid] = new CharacterInfo(row.Cid, row.Name ?? string.Empty, row.World ?? string.Empty);
        }
    }

    private sealed record Row(
        [property: JsonPropertyName("cid")]   ulong Cid,
        [property: JsonPropertyName("name")]  string? Name,
        [property: JsonPropertyName("world")] string? World);
}
