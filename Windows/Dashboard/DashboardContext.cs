using System;
using System.Collections.Generic;
using GilDelta.Characters;
using GilDelta.Events;
using GilDelta.Localization;
using GilDelta.Wallet;

namespace GilDelta.Windows.Dashboard;

public sealed class DashboardContext
{
    public IReadOnlyList<Wallet.Wallet> Wallets { get; }

    /// <summary>
    /// The same wallets, each still carrying which character it was read from and when.
    /// 🔴 <see cref="Wallets"/> is derived from this, not gathered separately, and
    /// <see cref="Total"/> is summed over <see cref="Wallets"/>. That is what lets the
    /// balance tab promise "these rows add up to the total": there is only one list,
    /// shown two ways.
    /// </summary>
    public IReadOnlyList<WalletOrigin> Origins { get; }

    public IReadOnlyList<GilEvent> Events { get; }
    public DateTimeOffset Now { get; }
    public Theme.Theme Theme { get; }

    public long Total { get; }

    /// <summary>Characters the picker offers; the unknown-owner bucket comes last when it has records.</summary>
    public IReadOnlyList<CharacterInfo> Characters { get; init; } = Array.Empty<CharacterInfo>();

    /// <summary>Display label per content id, so tabs never have to render a raw id.</summary>
    public IReadOnlyDictionary<ulong, string> CharacterLabels { get; init; } = new Dictionary<ulong, string>();

    /// <summary>Which character is being shown; null means every character on the account.</summary>
    public ulong? SelectedContentId { get; init; }

    public bool IsAccountWide => SelectedContentId is null;

    /// <summary>
    /// False when no wallet reading exists for the current selection. The total is
    /// then <em>unknown</em>, not zero, and the UI has to say so instead of printing 0.
    /// </summary>
    public bool HasWalletData { get; init; } = true;

    /// <summary>
    /// When the wallet figures were read. For the account-wide view this is the
    /// OLDEST reading across characters, i.e. the worst-case staleness.
    /// </summary>
    public DateTimeOffset? WalletReadingAt { get; init; }

    /// <summary>
    /// The daily settlements for this selection, oldest day first. A date that is not
    /// in here is a day the plugin never observed - which is NOT the same as a day
    /// whose net came to zero, and the chart draws the two differently.
    /// </summary>
    public IReadOnlyList<DailySummary> Daily { get; init; } = Array.Empty<DailySummary>();

    /// <summary>
    /// Records the user has excluded. They are deliberately NOT in <see cref="Events"/>,
    /// so no tab can count them by forgetting to filter; the timeline is the one view
    /// that reads this list, because it is where they can be put back.
    /// </summary>
    public IReadOnlyList<GilEvent> Excluded { get; init; } = Array.Empty<GilEvent>();

    /// <summary>
    /// Flips one record between counted and excluded. Null when there is no plugin behind
    /// the context - the tests build one directly - and the timeline then simply offers
    /// no button rather than pretending to.
    /// </summary>
    public Action<GilEvent>? ToggleExcluded { get; init; }

    /// <summary>
    /// Opens the detail view on one figure - "which records add up to this".
    /// Null when there is no window behind the context (the tests build one directly),
    /// and the tabs then simply do not offer the click rather than pretending to.
    /// </summary>
    public Action<DetailQuery>? ShowDetail { get; init; }

    /// <summary>
    /// True when the free company chest is being left out. The wallets and events
    /// handed to this context are ALREADY filtered; this flag is only so a view can
    /// say so on screen and pick the matching half of a <see cref="DailySummary"/>.
    /// </summary>
    public bool ExcludeFreeCompanyChest { get; init; }

    /// <summary>
    /// True when at least one wallet in this selection could not be read, which makes
    /// <see cref="Total"/> a lower bound rather than the whole figure. In practice this
    /// is the free company chest, which the game only reports while the player is at it.
    /// Distinct from <see cref="HasWalletData"/>: that one means "nothing was read at
    /// all", this one means "something was read but a part of it is missing".
    /// </summary>
    public bool HasUnknownWallet { get; }

    /// <summary>
    /// Settlement for one local day, or null when that day was never observed.
    /// Linear - fine for a one-off lookup. A view that walks a whole range should
    /// build its own map instead of calling this per day.
    /// </summary>
    public DailySummary? DayOf(DateOnly date)
    {
        foreach (var row in Daily)
            if (row.Date == date)
                return row;
        return null;
    }

    /// <summary>Label for a content id; falls back to something that still reads as an id, never a bare 0.</summary>
    public string LabelFor(ulong contentId) =>
        CharacterLabels.TryGetValue(contentId, out var label) ? label
        : contentId == CharacterInfo.UnknownContentId ? Strings.CharacterUnknown
        : $"#{contentId}";

    /// <summary>
    /// Convenience for tests and for any caller with no provenance to offer: the wallets
    /// are wrapped as origins with no character and no reading time, and the balance tab
    /// prints "?" for both rather than inventing them.
    /// </summary>
    public DashboardContext(
        IReadOnlyList<Wallet.Wallet> wallets,
        IReadOnlyList<GilEvent> events,
        DateTimeOffset now,
        Theme.Theme theme)
        : this(Unattributed(wallets), events, now, theme)
    {
    }

    private static List<WalletOrigin> Unattributed(IReadOnlyList<Wallet.Wallet> wallets)
    {
        var origins = new List<WalletOrigin>(wallets.Count);
        foreach (var w in wallets) origins.Add(new WalletOrigin(w, 0, null));
        return origins;
    }

    public DashboardContext(
        IReadOnlyList<WalletOrigin> origins,
        IReadOnlyList<GilEvent> events,
        DateTimeOffset now,
        Theme.Theme theme)
    {
        Origins = origins;

        // Derived, never passed in alongside: a caller that could hand over a wallet
        // list and a mismatched origin list would produce a breakdown that disagrees
        // with its own total, which is the single failure this view exists to avoid.
        var wallets = new List<Wallet.Wallet>(origins.Count);
        foreach (var o in origins) wallets.Add(o.Wallet);

        Wallets = wallets;
        Events  = events;
        Now     = now;
        Theme   = theme;

        long total = 0;
        var unknown = false;
        foreach (var w in wallets)
        {
            if (w.Amount is { } amount) total += amount;
            else unknown = true;
        }
        Total = total;
        HasUnknownWallet = unknown;
    }

    /// <summary>Net gil change on the local day containing <paramref name="day"/>.</summary>
    public long DailyNet(DateTimeOffset day)
    {
        var start = new DateTimeOffset(day.Date, day.Offset);
        var end   = start.AddDays(1);
        long sum  = 0;
        foreach (var ev in Events)
            if (ev.Timestamp >= start && ev.Timestamp < end)
                sum += ev.Amount;
        return sum;
    }

    /// <summary>Net gil change in [from, to], grouped by category.</summary>
    public IReadOnlyDictionary<GilEventCategory, long> NetByCategory(DateTimeOffset from, DateTimeOffset to)
    {
        var dict = new Dictionary<GilEventCategory, long>();
        foreach (var ev in Events)
        {
            if (ev.Timestamp < from || ev.Timestamp > to) continue;
            dict.TryGetValue(ev.Category, out var prev);
            dict[ev.Category] = prev + ev.Amount;
        }
        return dict;
    }
}
