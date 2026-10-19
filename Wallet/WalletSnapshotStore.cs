using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GilDelta.Wallet;

/// <summary>
/// Last-known wallet contents for every character on the account, so the dashboard
/// can show an account total instead of only the character currently logged in.
/// Mutated only from the framework thread; <see cref="Serialize"/> is taken there
/// too and handed to a background writer, so the dictionary is never walked while
/// a file is being written.
/// </summary>
public sealed class WalletSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<ulong, WalletSnapshot> _byCharacter = new();

    /// <summary>Where <see cref="Update"/> assembles a reading. Reused; never stored.</summary>
    private readonly List<Wallet> _merged = new();

    public int Count => _byCharacter.Count;

    /// <summary>
    /// Bumped whenever a stored snapshot is replaced. A pure timestamp refresh does not
    /// replace one (see <see cref="Update"/>), so an idle session leaves this alone.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>Every stored snapshot, newest reading first.</summary>
    public IReadOnlyList<WalletSnapshot> All =>
        _byCharacter.Values.OrderByDescending(s => s.At).ToList();

    public bool TryGet(ulong contentId, out WalletSnapshot snapshot) =>
        _byCharacter.TryGetValue(contentId, out snapshot!);

    /// <summary>
    /// Stores a fresh reading for one character.
    /// 🔴 A reading that does not mention a whole KIND of wallet the stored reading had
    /// is treated as "the game did not answer for that section", not as "those wallets
    /// are gone", and the stored rows are carried across.
    /// </summary>
    /// <returns>
    /// true when the contents actually differ, i.e. when the file is worth rewriting.
    /// A pure timestamp refresh returns false so an idle session does not rewrite it.
    /// </returns>
    public bool Update(ulong contentId, IReadOnlyList<Wallet> wallets, DateTimeOffset at)
    {
        _byCharacter.TryGetValue(contentId, out var previous);

        CarryForwardUnansweredKinds(previous?.Wallets, wallets, _merged);

        if (previous is not null && SameContents(previous.Wallets, _merged))
            return false;

        // A private copy: a stored snapshot is never edited once it is in the dictionary.
        _byCharacter[contentId] = new WalletSnapshot(contentId, at, _merged.ToArray());
        Version++;
        return true;
    }

    /// <summary>
    /// The fresh reading, plus any whole wallet kind the stored reading had and the
    /// fresh one does not mention at all. Fresh rows always win; carried rows go on the
    /// end, so a later complete reading restores the reader's own order.
    /// </summary>
    /// <remarks>
    /// By index throughout: this runs every tick, and enumerating a list through its
    /// interface boxes an enumerator each time.
    /// </remarks>
    private static void CarryForwardUnansweredKinds(
        IReadOnlyList<Wallet>? previous, IReadOnlyList<Wallet> fresh, List<Wallet> merged)
    {
        merged.Clear();
        for (var i = 0; i < fresh.Count; i++) merged.Add(fresh[i]);
        if (previous is null || previous.Count == 0) return;

        foreach (var kind in Kinds)
        {
            if (Mentions(fresh, kind) || !Mentions(previous, kind)) continue;

            for (var i = 0; i < previous.Count; i++)
                if (previous[i].Id.Kind == kind)
                    merged.Add(previous[i]);
        }
    }

    /// <summary>Enumerated once: <c>Enum.GetValues</c> per tick would allocate.</summary>
    private static readonly WalletKind[] Kinds =
    {
        WalletKind.Self, WalletKind.Retainer, WalletKind.FreeCompanyChest,
    };

    private static bool Mentions(IReadOnlyList<Wallet> wallets, WalletKind kind)
    {
        for (var i = 0; i < wallets.Count; i++)
            if (wallets[i].Id.Kind == kind) return true;
        return false;
    }

    internal static bool SameContents(IReadOnlyList<Wallet> a, IReadOnlyList<Wallet> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (!a[i].Equals(b[i])) return false;
        return true;
    }

    /// <summary>
    /// Flattens snapshots into one wallet list for an account-wide total.
    /// 🔴 "Newest wins" is qualified by "known beats unknown": the chest is unreadable
    /// unless the player is standing at it, so the newest character to log in almost
    /// always carries an unknown chest reading, and letting that displace a real figure
    /// would drop the chest out of the account total. An unknown row is still kept when
    /// it is all we have, so the view can say "?".
    /// </summary>
    public static List<Wallet> Merge(IEnumerable<WalletSnapshot> snapshots)
    {
        var detailed = MergeDetailed(snapshots);
        var merged = new List<Wallet>(detailed.Count);
        foreach (var origin in detailed) merged.Add(origin.Wallet);
        return merged;
    }

    /// <summary>
    /// 🔴 <see cref="Merge"/> is a projection of this rather than a second copy of the
    /// algorithm, deliberately. The flat list is what the total is summed over and this
    /// one is what claims to explain that total; two implementations of "which wallets
    /// count" would drift, and the symptom on screen would be a breakdown whose rows do
    /// not add up to the number printed above them.
    /// </summary>
    public static List<WalletOrigin> MergeDetailed(IEnumerable<WalletSnapshot> snapshots)
    {
        var ordered = NewestFirst(snapshots);

        // Upper bound: only duplicate chest rows are dropped, so one array and no regrowth.
        var capacity = 0;
        for (var i = 0; i < ordered.Count; i++) capacity += ordered[i].Wallets.Count;

        var merged = new List<WalletOrigin>(capacity);
        // FC name -> index in merged of the row currently representing that company.
        var freeCompanyRow = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var s = 0; s < ordered.Count; s++)
        {
            var snapshot = ordered[s];
            var wallets = snapshot.Wallets;
            for (var w = 0; w < wallets.Count; w++)
            {
                var wallet = wallets[w];
                var row = new WalletOrigin(wallet, snapshot.ContentId, snapshot.At);

                if (wallet.Id.Kind != WalletKind.FreeCompanyChest)
                {
                    merged.Add(row);
                    continue;
                }

                if (!freeCompanyRow.TryGetValue(wallet.Id.Identifier, out var index))
                {
                    freeCompanyRow[wallet.Id.Identifier] = merged.Count;
                    merged.Add(row);
                    continue;
                }

                // Already holding a row for this company. Only upgrade it, never
                // downgrade: a real figure replaces a placeholder, nothing replaces a
                // real figure (the first one seen is already the newest). The origin
                // travels with the figure, so the row describes the reading that
                // actually saw into the chest rather than whichever character we
                // happened to meet the company through first.
                if (!merged[index].Wallet.IsKnown && wallet.IsKnown) merged[index] = row;
            }
        }

        return merged;
    }

    /// <summary>
    /// The snapshots in the order <see cref="MergeDetailed"/> walks them. A list that is
    /// already newest first (what <see cref="All"/> hands out) is used as is: the sort is
    /// stable, so re-sorting it would return this very order.
    /// </summary>
    private static IReadOnlyList<WalletSnapshot> NewestFirst(IEnumerable<WalletSnapshot> snapshots)
    {
        if (snapshots is IReadOnlyList<WalletSnapshot> list)
        {
            var sorted = true;
            for (var i = 1; i < list.Count && sorted; i++)
                sorted = list[i - 1].At >= list[i].At;
            if (sorted) return list;
        }

        return snapshots.OrderByDescending(s => s.At).ToArray();
    }

    /// <summary>Oldest reading among the given snapshots — i.e. how stale the total is.</summary>
    public static DateTimeOffset? OldestAt(IEnumerable<WalletSnapshot> snapshots)
    {
        DateTimeOffset? oldest = null;
        foreach (var snapshot in snapshots)
            if (oldest is null || snapshot.At < oldest) oldest = snapshot.At;
        return oldest;
    }

    public string Serialize() =>
        JsonSerializer.Serialize(
            _byCharacter.Values
                .OrderBy(s => s.ContentId)
                .Select(s => new Row(
                    s.ContentId,
                    s.At,
                    s.Wallets.Select(w => new WalletRow(w.Id.Kind, w.Id.Identifier, w.Amount)).ToList()))
                .ToList(),
            JsonOpts);

    /// <summary>
    /// Merges a persisted file in. Malformed JSON is ignored rather than thrown — a
    /// broken totals cache must never stop the event log from loading.
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
            var wallets = (row.Wallets ?? new List<WalletRow>())
                .Select(w => new Wallet(new WalletId(w.Kind, w.Id ?? string.Empty), w.Amount))
                .ToList();
            _byCharacter[row.Cid] = new WalletSnapshot(row.Cid, row.At, wallets);
            Version++;
        }
    }

    private sealed record Row(
        [property: JsonPropertyName("cid")]     ulong Cid,
        [property: JsonPropertyName("at")]      DateTimeOffset At,
        [property: JsonPropertyName("wallets")] List<WalletRow>? Wallets);

    private sealed record WalletRow(
        [property: JsonPropertyName("kind")]   WalletKind Kind,
        [property: JsonPropertyName("id")]     string? Id,
        [property: JsonPropertyName("amount")] long? Amount);
}
