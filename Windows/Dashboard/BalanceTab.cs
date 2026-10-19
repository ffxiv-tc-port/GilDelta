using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using GilDelta.Localization;
using GilDelta.Wallet;

namespace GilDelta.Windows.Dashboard;

/// <summary>
/// The wallets behind the total, one row each: which wallet, how much, and when that
/// figure was read. Deliberately NOT folded into <see cref="DetailWindow"/>: that view
/// answers "which RECORDS add up to this", while a balance is a reading of one wallet at
/// a moment, with its own way of being unknown (a "?") and of being stale.
/// 🔴 Taken from the context provider every frame, and rebuilt whenever it hands over a
/// new wallet list: a snapshot would sit there explaining a number that is no longer on screen.
/// </summary>
public sealed class BalanceTab : IDashboardTab
{
    public DashboardTab Identity => DashboardTab.Balances;
    public string Title => Strings.BalanceTitle;

    /// <summary>The balance column's ImGui user id; the sort specs name their column by it.</summary>
    private const uint BalanceColumnId = 1;

    private readonly Configuration _config;
    private readonly BalanceRowCache _rows = new();

    private string _hideZeroLabel = string.Empty;
    private int _hideZeroLabelCount = -1;
    private Language _hideZeroLabelLanguage;

    public BalanceTab(Configuration config) => _config = config;

    public void Draw(DashboardContext ctx)
    {
        var set = _rows.For(ctx.Origins);
        if (set.All.Count == 0)
        {
            ImGui.TextDisabled(Strings.BalanceNoWallets);
            return;
        }

        DrawHeader(ctx, set.All);
        DrawHideZeroToggle(set.ZeroCount);
        DrawTable(ctx, set);
        DrawFooter(set.All);
    }

    /// <summary>
    /// 🔴 Only wallets read as exactly 0 are hidden; a "?" is not 0 and stays. The count is
    /// how many wallets the box concerns, shown whichever way it is set.
    /// </summary>
    private void DrawHideZeroToggle(int zeroCount)
    {
        var hide = _config.HideZeroBalances;
        if (ImGui.Checkbox(HideZeroLabel(zeroCount), ref hide)) _config.HideZeroBalances = hide;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.BalanceHideZeroHelp);
    }

    /// <summary>Formatted only when the count or the language moves. "###" keeps the widget id fixed while the count changes.</summary>
    private string HideZeroLabel(int zeroCount)
    {
        if (zeroCount == _hideZeroLabelCount && Strings.Current == _hideZeroLabelLanguage)
            return _hideZeroLabel;

        _hideZeroLabel = string.Format(Strings.BalanceHideZero, zeroCount) + "###gdhidezero";
        _hideZeroLabelCount = zeroCount;
        _hideZeroLabelLanguage = Strings.Current;
        return _hideZeroLabel;
    }

    /// <summary>
    /// Whose wallets these are. Kept even though the dashboard has a character picker
    /// directly above the tab bar: the picker's default reads "follow current character",
    /// which does not say WHICH character that currently is.
    /// </summary>
    private static void DrawHeader(DashboardContext ctx, IReadOnlyList<WalletOrigin> rows)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
        ImGui.TextUnformatted(ctx.IsAccountWide
            ? Strings.CharacterAll
            : ctx.LabelFor(rows[0].ContentId));
        ImGui.PopStyleColor();

        // Whether the chest is being counted changes what this list IS, so it goes on
        // the row rather than into a tooltip; the tooltip only explains why anyone
        // would want it left out.
        if (!ctx.ExcludeFreeCompanyChest) return;

        ImGui.SameLine();
        ImGui.TextDisabled(Strings.ExcludingFcChestNow);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.ExcludeFcChestHelp);
    }

    private void DrawTable(DashboardContext ctx, BalanceRowSet set)
    {
        // The owning character only earns a column when more than one is on screen; in
        // the single-character view it would be the same value on every row, and the
        // header line above already says whose it is.
        // Counted over every row, hidden ones included: under "All characters" the rows
        // left after hiding zeros still need to say whose they are.
        var showCharacter = set.CharacterCount > 1;
        var columns = 3 + (showCharacter ? 1 : 0);

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders
                  | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY
                  | ImGuiTableFlags.Sortable | ImGuiTableFlags.SortTristate;
        if (!ImGui.BeginTable("##gdbalances", columns, flags)) return;

        const ImGuiTableColumnFlags fixedNoSort = ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort;
        if (showCharacter)
            ImGui.TableSetupColumn(Strings.CharacterLabel, fixedNoSort, 150);
        ImGui.TableSetupColumn(Strings.ColumnWallet, ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort);
        ImGui.TableSetupColumn(Strings.BalanceColumnAmount,
            ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.PreferSortDescending, 110, BalanceColumnId);
        ImGui.TableSetupColumn(Strings.BalanceColumnReadAt, fixedNoSort, 110);

        ShowSortFromConfig(balanceColumn: showCharacter ? 2 : 1);
        ImGui.TableHeadersRow();
        TakeSortClick();

        // Picked after the header row and the checkbox above, so either click shows on this very frame.
        var rows = set.Shown(_config.HideZeroBalances, _config.BalanceSortOrder);
        for (var i = 0; i < rows.Count; i++) DrawRow(ctx, rows[i], showCharacter);

        // Over every row: a hidden row is exactly 0, so hiding cannot move the sum.
        DrawSumRow(ctx, set.All, showCharacter);

        ImGui.EndTable();
    }

    /// <summary>
    /// Makes the header arrow match the saved order before the header row is drawn: ImGui
    /// forgets its sort state whenever it rebuilds the table (first show, or the character
    /// column coming or going), so the setting is the authority. Also clears whatever is
    /// pending, so after the header row a dirty flag can only mean a click.
    /// </summary>
    private void ShowSortFromConfig(int balanceColumn)
    {
        var specs = ImGui.TableGetSortSpecs();
        if (specs.IsNull) return;

        var wanted = _config.BalanceSortOrder;
        if (ShownSort(specs) != wanted)
        {
            ImGuiP.TableSetColumnSortDirection(balanceColumn, ToDirection(wanted), false);
            specs = ImGui.TableGetSortSpecs();
        }
        specs.SpecsDirty = false;
    }

    /// <summary>Every other column is NoSort, so a dirty flag here is a click on the balance header.</summary>
    private void TakeSortClick()
    {
        var specs = ImGui.TableGetSortSpecs();
        if (specs.IsNull || !specs.SpecsDirty) return;

        specs.SpecsDirty = false;
        _config.BalanceSortOrder = _config.BalanceSortOrder.Next();
    }

    private static BalanceSort ShownSort(ImGuiTableSortSpecsPtr specs)
    {
        // With SortTristate "no sort" is a count of 0, and Specs is then a null pointer.
        if (specs.SpecsCount < 1 || specs.Specs.IsNull || specs.Specs.ColumnUserID != BalanceColumnId)
            return BalanceSort.Default;

        return specs.Specs.SortDirection switch
        {
            ImGuiSortDirection.Descending => BalanceSort.AmountDescending,
            ImGuiSortDirection.Ascending  => BalanceSort.AmountAscending,
            _                             => BalanceSort.Default,
        };
    }

    private static ImGuiSortDirection ToDirection(BalanceSort sort) => sort switch
    {
        BalanceSort.AmountDescending => ImGuiSortDirection.Descending,
        BalanceSort.AmountAscending  => ImGuiSortDirection.Ascending,
        _                            => ImGuiSortDirection.None,
    };

    private static void DrawRow(DashboardContext ctx, WalletOrigin row, bool showCharacter)
    {
        ImGui.TableNextRow();

        if (showCharacter)
        {
            ImGui.TableNextColumn();
            Text(ctx.Theme.TextPrimary, ctx.LabelFor(row.ContentId));
        }

        ImGui.TableNextColumn();
        Text(ctx.Theme.TextPrimary, WalletLabel(row.Wallet.Id));

        ImGui.TableNextColumn();
        DrawAmount(ctx, row);

        ImGui.TableNextColumn();
        DrawReadAt(ctx, row);
    }

    /// <summary>
    /// 🔴 "?" for a wallet that could not be read, never 0. An unreadable free company
    /// chest reported as a flat 0 is how the next real reading becomes a fabricated deposit;
    /// a row showing 0 here would be making that same claim.
    /// </summary>
    private static void DrawAmount(DashboardContext ctx, WalletOrigin row)
    {
        if (row.Wallet.Amount is { } amount)
        {
            Text(ctx.Theme.TextPrimary, $"{amount:N0}");
            return;
        }

        ImGui.TextDisabled("?");
        if (!ImGui.IsItemHovered()) return;
        ImGui.SetTooltip(row.Wallet.Id.Kind == WalletKind.FreeCompanyChest
            ? Strings.FcChestUnreadableHelp
            : Strings.BalanceUnreadableHelp);
    }

    /// <summary>
    /// When this figure was read. "Now" only for a wallet the game answered for on this
    /// very frame - see <see cref="WalletOrigin.IsLive"/> for why that is not the same
    /// as "belongs to the character being played".
    /// </summary>
    private static void DrawReadAt(DashboardContext ctx, WalletOrigin row)
    {
        if (row.IsLive)
        {
            Text(ctx.Theme.TextMuted, Strings.BalanceLiveNow);
            return;
        }

        if (row.At is not { } at)
        {
            ImGui.TextDisabled("?");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.BalanceReadAtUnknownHelp);
            return;
        }

        var local = at.ToLocalTime();
        ImGui.TextDisabled(local.ToString("MM-dd HH:mm"));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{local:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{Strings.BalanceStaleHelp}");
    }

    /// <summary>
    /// The sum of the rows above.
    /// No "does this reconcile" line, unlike the detail view: there is nothing to
    /// reconcile against. <c>DashboardContext.Total</c> is summed over the very list
    /// these rows were built from - <c>Wallets</c> is derived from <c>Origins</c>, not
    /// gathered beside it - so the two are the same arithmetic over the same data and
    /// cannot drift. A difference line here would be code that can never run.
    /// </summary>
    private static void DrawSumRow(DashboardContext ctx, IReadOnlyList<WalletOrigin> rows, bool showCharacter)
    {
        ImGui.TableNextRow();

        if (showCharacter) ImGui.TableNextColumn();

        ImGui.TableNextColumn();
        Text(ctx.Theme.TextAccent, Strings.BalanceSumRow);

        ImGui.TableNextColumn();
        Text(ctx.Theme.TextAccent, $"{BalanceRows.Total(rows):N0}");

        ImGui.TableNextColumn();
    }

    /// <summary>
    /// How many wallets are missing from the sum, on the row rather than in a tooltip:
    /// "the total above is a lower bound" is the fact, and a fact hidden behind a hover
    /// is a fact nobody knows to go looking for.
    /// </summary>
    private static void DrawFooter(IReadOnlyList<WalletOrigin> rows)
    {
        var unknown = BalanceRows.UnknownCount(rows);
        if (unknown == 0) return;

        ImGui.TextDisabled($"{unknown} {Strings.BalanceUnknownCount}");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.BalanceUnknownCountHelp);
    }

    private static void Text(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static string WalletLabel(WalletId id) => id.Kind switch
    {
        WalletKind.Self     => Strings.WalletSelf,
        WalletKind.Retainer => $"{Strings.WalletRetainerPrefix}{id.Identifier}",
        WalletKind.FreeCompanyChest => id.Identifier.Length > 0
            ? $"{Strings.WalletFcChest} ({id.Identifier})"
            : Strings.WalletFcChest,
        _ => id.Kind.ToString(),
    };
}
