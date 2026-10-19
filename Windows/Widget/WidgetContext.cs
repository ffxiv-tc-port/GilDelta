using System;
using System.Collections.Generic;
using System.Linq;
using GilDelta.Characters;
using GilDelta.Events;
using GilDelta.Localization;
using GilDelta.Theme;
using GilDelta.Wallet;

namespace GilDelta.Windows.Widget;

public sealed class WidgetContext
{
    public IReadOnlyList<Wallet.Wallet> Wallets { get; }

    /// <summary>
    /// The same wallets, each still carrying which character it was read from and when.
    /// 🔴 <see cref="Wallets"/> is derived from this, not gathered separately, and every
    /// figure on this object is summed over <see cref="Wallets"/>. That is what makes the
    /// balance rows able to promise "these rows add up to the total above them": there is
    /// only one list, shown two ways.
    /// </summary>
    public IReadOnlyList<WalletOrigin> Origins { get; }

    public IReadOnlyList<GilEvent> RecentEvents { get; }

    /// <summary>
    /// When the context was built. The plugin reuses a context for up to a minute, so a
    /// view that needs the time of this frame reads the clock itself.
    /// </summary>
    public DateTimeOffset Now { get; }

    public Theme.Theme Theme { get; }

    public long Total { get; }
    public long SelfTotal { get; }
    public long RetainerTotal { get; }

    /// <summary>
    /// The free company chest, or <c>null</c> when it could not be read. 🔴 Null is not
    /// the same as 0: the game only reports the chest while the player is standing at
    /// it, so a 0 here would mean "we did not look" far more often than "it is empty".
    /// Renderers must print "?" for null, never a number.
    /// </summary>
    public long? FreeCompanyTotal { get; }

    /// <summary>
    /// True when any wallet behind these figures could not be read, making
    /// <see cref="Total"/> a lower bound.
    /// </summary>
    public bool HasUnknownWallet { get; }

    public long TodayDelta { get; }
    public long WeekDelta  { get; }
    public long MonthDelta { get; }

    /// <summary>
    /// True when these figures cover every character on the account rather than just
    /// the one being played. The window surfaces it as a hover tooltip so an
    /// account-wide total is never mistaken for the current character's purse.
    /// </summary>
    public bool IsAccountWide { get; init; }

    /// <summary>
    /// True when the free company chest was filtered out before these figures were
    /// built. Renderers need to know, because <see cref="FreeCompanyTotal"/> is then 0
    /// for the wrong reason — the chest is not empty, it is not being counted — and a
    /// bare 0 on screen says the opposite.
    /// </summary>
    public bool ExcludeFreeCompanyChest { get; init; }

    /// <summary>Display label per content id, so the balance rows never render a raw id.</summary>
    public IReadOnlyDictionary<ulong, string> CharacterLabels { get; init; } =
        new Dictionary<ulong, string>();

    /// <summary>
    /// Opens the dashboard on its daily settlement tab. Not a toggle, and not the
    /// balance list: the wallets behind this figure are a tab of that same dashboard.
    /// Null when there is no window behind the context (the tests build one directly),
    /// and the renderers then simply do not offer the click rather than pretending to.
    /// </summary>
    public Action? OpenDailySettlement { get; init; }

    /// <summary>
    /// Set by whichever renderer drew the total, read by the window right afterwards.
    /// Draw-thread scratch space, not state: the window clears it before every frame's draw
    /// because the context itself is reused across frames. The window owns the widget's
    /// single tooltip and has to know whether the pointer is on the total; a second
    /// <c>SetTooltip</c> from the renderer would simply be overwritten.
    /// </summary>
    public bool TotalHovered { get; set; }

    public string LabelFor(ulong contentId) =>
        CharacterLabels.TryGetValue(contentId, out var label) ? label
        : contentId == CharacterInfo.UnknownContentId ? Strings.CharacterUnknown
        : $"#{contentId}";

    /// <summary>
    /// Convenience for tests and for any caller that has no provenance to offer: the
    /// wallets are wrapped as origins with no character and no reading time, and the
    /// balance rows print "?" for both rather than inventing them.
    /// </summary>
    public WidgetContext(
        IReadOnlyList<Wallet.Wallet> wallets,
        IReadOnlyList<GilEvent> recentEvents,
        DateTimeOffset now,
        Theme.Theme theme)
        : this(Unattributed(wallets), recentEvents, now, theme)
    {
    }

    private static List<WalletOrigin> Unattributed(IReadOnlyList<Wallet.Wallet> wallets)
    {
        var origins = new List<WalletOrigin>(wallets.Count);
        foreach (var w in wallets) origins.Add(new WalletOrigin(w, 0, null));
        return origins;
    }

    public WidgetContext(
        IReadOnlyList<WalletOrigin> origins,
        IReadOnlyList<GilEvent> recentEvents,
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
        RecentEvents = recentEvents;
        Now = now;
        Theme = theme;

        long total = 0, self = 0, retainer = 0, freeCompany = 0;
        var anyUnknown = false;
        var freeCompanyUnknown = false;

        foreach (var w in wallets)
        {
            if (w.Amount is not { } amount)
            {
                anyUnknown = true;
                if (w.Id.Kind == WalletKind.FreeCompanyChest) freeCompanyUnknown = true;
                continue;
            }

            total += amount;
            switch (w.Id.Kind)
            {
                case WalletKind.Self:             self        += amount; break;
                case WalletKind.Retainer:         retainer    += amount; break;
                case WalletKind.FreeCompanyChest: freeCompany += amount; break;
            }
        }

        Total            = total;
        SelfTotal        = self;
        RetainerTotal    = retainer;
        // One unreadable chest makes the whole chest figure unknown - partially summing
        // several companies would print a number that is nobody's actual balance.
        FreeCompanyTotal = freeCompanyUnknown ? null : freeCompany;
        HasUnknownWallet = anyUnknown;

        var dayStart   = new DateTimeOffset(now.Date, now.Offset);
        var weekStart  = dayStart.AddDays(-((int)now.DayOfWeek));
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);

        TodayDelta = recentEvents.Where(e => e.Timestamp >= dayStart  ).Sum(e => e.Amount);
        WeekDelta  = recentEvents.Where(e => e.Timestamp >= weekStart ).Sum(e => e.Amount);
        MonthDelta = recentEvents.Where(e => e.Timestamp >= monthStart).Sum(e => e.Amount);
    }
}
