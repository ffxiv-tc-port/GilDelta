using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GilDelta.Events;

/// <summary>
/// The daily settlement book: one <see cref="DailySummary"/> per character per local
/// day, persisted as JSONL under <c>&lt;pluginConfigDir&gt;/daily/&lt;contentId&gt;.jsonl</c>.
/// A settled day survives retention, and a day with no row is a day the plugin never
/// saw - a different thing from a day whose net came to zero.
/// 🔴 Framework thread only. <see cref="Serialize"/> is taken there too and handed to a
/// background writer, so no dictionary is ever walked while a file is being written.
/// </summary>
public sealed class DailyLedger
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Dictionary<ulong, SortedDictionary<DateOnly, DailySummary>> _byCharacter = new();

    /// <summary>Characters that have at least one settled day.</summary>
    public IReadOnlyCollection<ulong> ContentIds => _byCharacter.Keys;

    /// <summary>
    /// The local day an event belongs to. The event carries the offset it was recorded
    /// with, so this is the wall clock the player actually saw - it does not drift if
    /// the machine's time zone changes later.
    /// </summary>
    public static DateOnly LocalDayOf(DateTimeOffset timestamp) => DateOnly.FromDateTime(timestamp.Date);

    public bool TryGet(ulong contentId, DateOnly date, out DailySummary summary)
    {
        summary = null!;
        return _byCharacter.TryGetValue(contentId, out var days) && days.TryGetValue(date, out summary!);
    }

    public DailySummary GetOrCreate(ulong contentId, DateOnly date)
    {
        if (!_byCharacter.TryGetValue(contentId, out var days))
        {
            days = new SortedDictionary<DateOnly, DailySummary>();
            _byCharacter[contentId] = days;
        }

        if (!days.TryGetValue(date, out var summary))
        {
            summary = new DailySummary(contentId, date);
            days[date] = summary;
        }

        return summary;
    }

    /// <summary>
    /// Whether an event belongs in the settlement book at all. A cross-character
    /// transfer does not: the same gil shows up on both characters, so booking it
    /// would count one move as an expense and as an income. It stays in the event
    /// log either way - this is a bookkeeping rule, not a deletion.
    /// </summary>
    public static bool IsBooked(GilEvent ev) =>
        ev.Category != GilEventCategory.CharacterTransfer;

    /// <summary>Books one gil event into the day its own timestamp falls on.</summary>
    public void Add(GilEvent ev)
    {
        if (!IsBooked(ev)) return;
        GetOrCreate(ev.ContentId, LocalDayOf(ev.Timestamp)).Add(ev);
    }

    /// <summary>
    /// Takes an event back out of the day it was booked into, for a record the user has
    /// excluded. Returns false when that day has no row - nothing to adjust.
    /// </summary>
    public bool Retract(GilEvent ev)
    {
        // Symmetrical with Add: what was never booked must never be taken back out.
        if (!IsBooked(ev)) return false;
        if (!TryGet(ev.ContentId, LocalDayOf(ev.Timestamp), out var row)) return false;
        row.Retract(ev);
        return true;
    }

    /// <summary>
    /// Puts a previously excluded event back into its day - the exact inverse of
    /// <see cref="Retract"/>, so the user can change their mind.
    /// </summary>
    public void Reinstate(GilEvent ev) => Add(ev);

    /// <summary>Records a wallet reading. Returns true when the day's row changed.</summary>
    public bool ObserveTotal(ulong contentId, DateOnly date, long total, long totalExcludingFreeCompany) =>
        GetOrCreate(contentId, date).ObserveTotal(total, totalExcludingFreeCompany);

    /// <summary>
    /// One character's days, oldest first. A null scope folds every character together
    /// by date - see <see cref="DailySummary.IsAggregate"/> for what that does to the
    /// opening and closing totals.
    /// </summary>
    public IReadOnlyList<DailySummary> For(ulong? contentId)
    {
        if (contentId is { } cid)
        {
            if (!_byCharacter.TryGetValue(cid, out var days)) return Array.Empty<DailySummary>();
            return new List<DailySummary>(days.Values);
        }

        var byDate = new SortedDictionary<DateOnly, List<DailySummary>>();
        foreach (var days in _byCharacter.Values)
        {
            foreach (var row in days.Values)
            {
                if (!byDate.TryGetValue(row.Date, out var bucket))
                {
                    bucket = new List<DailySummary>();
                    byDate[row.Date] = bucket;
                }
                bucket.Add(row);
            }
        }

        var merged = new List<DailySummary>(byDate.Count);
        foreach (var kv in byDate)
        {
            // A date only one character touched still goes through Aggregate, so the
            // account-wide view never shows one character's opening total as if it
            // were the account's.
            merged.Add(DailySummary.Aggregate(kv.Key, kv.Value));
        }
        return merged;
    }

    /// <summary>
    /// Fills the book in from the event log. A day that already has a row is left alone:
    /// the stored row is the authority, which is what makes settled days outlive the
    /// events behind them. The single exception is <paramref name="today"/>, whose row is
    /// rebuilt from the log every time - the day is still running, and rebuilding heals
    /// the case where the game went down between an event being appended and the
    /// settlement being written out. Wallet readings are kept either way.
    /// </summary>
    /// <returns>The characters whose rows changed, i.e. whose file needs rewriting.</returns>
    public IReadOnlyCollection<ulong> BackfillFromEvents(IEnumerable<GilEvent> events, DateOnly today)
    {
        var touched = new HashSet<ulong>();
        var accepting = new HashSet<(ulong, DateOnly)>();
        var settled = new HashSet<(ulong, DateOnly)>();

        foreach (var ev in events)
        {
            // A record the user has excluded is not booked at all, so rebuilding today's
            // row from the log agrees with the adjustment Retract already made to it.
            if (ev.Suspect) continue;
            if (!IsBooked(ev)) continue;

            var key = (ev.ContentId, LocalDayOf(ev.Timestamp));
            if (settled.Contains(key)) continue;

            // The decision is taken once per (character, day) and remembered. Asking
            // TryGet per event would flip to "already known" the moment this pass
            // created the row, and every event after the first would be dropped.
            if (!accepting.Contains(key))
            {
                if (TryGet(key.Item1, key.Item2, out _))
                {
                    if (key.Item2 != today)
                    {
                        settled.Add(key);
                        continue;
                    }
                    GetOrCreate(key.Item1, key.Item2).ResetEvents();
                }
                accepting.Add(key);
            }

            GetOrCreate(key.Item1, key.Item2).Add(ev);
            touched.Add(ev.ContentId);
        }

        return touched;
    }

    /// <summary>Renders one character's whole book, oldest day first.</summary>
    public string Serialize(ulong contentId)
    {
        if (!_byCharacter.TryGetValue(contentId, out var days)) return string.Empty;

        var sb = new StringBuilder();
        foreach (var row in days.Values)
        {
            if (row.IsEmpty) continue;
            sb.Append(JsonSerializer.Serialize(StoredDay.From(row, CurrentSchemaVersion), JsonOpts));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Merges a persisted file in. A malformed line is skipped rather than thrown: one
    /// bad day must never cost the rest of the book.
    /// </summary>
    /// <param name="ownerContentId">
    /// The character the file belongs to, used for rows written without a <c>cid</c>.
    /// </param>
    public void LoadFrom(ulong ownerContentId, string jsonl)
    {
        foreach (var line in jsonl.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            StoredDay? record;
            try
            {
                record = JsonSerializer.Deserialize<StoredDay>(trimmed, JsonOpts);
            }
            catch
            {
                continue;
            }

            if (record is null) continue;
            if (record.V != CurrentSchemaVersion) continue;
            if (!DateOnly.TryParse(record.Date, out var date)) continue;

            var cid = record.Cid ?? ownerContentId;
            var row = DailySummary.Restore(
                cid,
                date,
                ParseCategories(record.ByCategory),
                record.Events,
                ParseCategories(record.FcByCategory),
                record.FcEvents,
                record.Open,
                record.Close,
                record.OpenExFc,
                record.CloseExFc);

            if (!_byCharacter.TryGetValue(cid, out var days))
            {
                days = new SortedDictionary<DateOnly, DailySummary>();
                _byCharacter[cid] = days;
            }
            days[date] = row;
        }
    }

    /// <summary>
    /// Category names are stored as text, exactly like the event log, so the ordinal
    /// positions stay free to move. A name this build does not know is dropped: keeping
    /// it would mean inventing a category, and folding it into Misc would misattribute
    /// gil that a newer build classified deliberately.
    /// </summary>
    private static IEnumerable<KeyValuePair<GilEventCategory, long>> ParseCategories(
        Dictionary<string, long>? raw)
    {
        if (raw is null) yield break;

        foreach (var kv in raw)
        {
            // Numeric forms are rejected on purpose, the same way the IPC endpoint
            // rejects them: Enum.TryParse("3") would otherwise succeed and quietly
            // book the gil under whichever member happens to sit at ordinal 3.
            if (kv.Key.Length == 0 || !char.IsLetter(kv.Key[0])) continue;

            if (Enum.TryParse<GilEventCategory>(kv.Key, ignoreCase: false, out var category) &&
                Enum.IsDefined(category))
            {
                yield return new KeyValuePair<GilEventCategory, long>(category, kv.Value);
            }
        }
    }

    private static Dictionary<string, long>? Render(IReadOnlyDictionary<GilEventCategory, long> buckets)
    {
        if (buckets.Count == 0) return null;

        var map = new Dictionary<string, long>(buckets.Count);
        foreach (var kv in buckets) map[kv.Key.ToString()] = kv.Value;
        return map;
    }

    /// <summary>Internal DTO for JSON (de)serialization.</summary>
    private sealed record StoredDay(
        [property: JsonPropertyName("v")]          int V,
        [property: JsonPropertyName("date")]       string Date,
        [property: JsonPropertyName("cid")]        ulong? Cid,
        [property: JsonPropertyName("events")]     int Events,
        [property: JsonPropertyName("byCategory")] Dictionary<string, long>? ByCategory,
        [property: JsonPropertyName("fcEvents")]   int FcEvents,
        [property: JsonPropertyName("fcByCategory")] Dictionary<string, long>? FcByCategory,
        [property: JsonPropertyName("open")]       long? Open,
        [property: JsonPropertyName("close")]      long? Close,
        [property: JsonPropertyName("openExFc")]   long? OpenExFc,
        [property: JsonPropertyName("closeExFc")]  long? CloseExFc)
    {
        public static StoredDay From(DailySummary row, int version) => new(
            version,
            row.Date.ToString("yyyy-MM-dd"),
            row.ContentId != 0 ? row.ContentId : null,
            row.EventCount,
            Render(row.RawCategoryTotals),
            row.FreeCompanyEventCount,
            Render(row.RawFreeCompanyCategoryTotals),
            row.OpenTotal,
            row.CloseTotal,
            row.OpenTotalExcludingFreeCompany,
            row.CloseTotalExcludingFreeCompany);
    }
}
