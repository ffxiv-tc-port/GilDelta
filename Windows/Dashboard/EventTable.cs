using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using GilDelta.Events;
using GilDelta.Localization;

namespace GilDelta.Windows.Dashboard;

/// <summary>
/// The one place a list of records is drawn. The timeline and the detail view show the
/// same thing - individual records, with the excluded ones greyed out and reversible -
/// and two copies of that would drift: the moment the detail view labelled a category
/// differently from the tab above it, the user would have no way to tell which of the
/// two was wrong.
/// </summary>
public static class EventTable
{
    /// <summary>
    /// Draws the rows and applies at most one exclude/restore press per frame.
    /// <para>
    /// 🔴 The press is acted on AFTER <c>EndTable</c>, never inside it: the callback
    /// rewrites the log this row list was built from, and ImGui is mid-table.
    /// </para>
    /// </summary>
    /// <param name="idPrefix">Keeps the per-row button ids apart between callers.</param>
    public static void Draw(
        DashboardContext ctx, IReadOnlyList<GilEvent> rows, string idPrefix, bool scrollY)
    {
        // The owning character only earns a column when several characters are on
        // screen at once; in single-character view it would be the same value on
        // every row.
        var showCharacter = ctx.IsAccountWide;
        var showActions = ctx.ToggleExcluded is not null;

        var columns = 4 + (showCharacter ? 1 : 0) + (showActions ? 1 : 0);

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;
        if (scrollY) flags |= ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable($"##{idPrefix}table", columns, flags)) return;

        ImGui.TableSetupColumn(Strings.ColumnWhen,     ImGuiTableColumnFlags.WidthFixed, 130);
        if (showCharacter)
            ImGui.TableSetupColumn(Strings.CharacterLabel, ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn(Strings.ColumnCategory, ImGuiTableColumnFlags.WidthFixed, 130);
        ImGui.TableSetupColumn(Strings.ColumnWallet,   ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Strings.ColumnAmount,   ImGuiTableColumnFlags.WidthFixed, 110);
        if (showActions)
            ImGui.TableSetupColumn(Strings.ColumnActions, ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableHeadersRow();

        GilEvent? pressed = null;

        for (var i = 0; i < rows.Count; i++)
        {
            var ev = rows[i];

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
            ImGui.TextUnformatted(ev.Timestamp.ToString("MM-dd HH:mm:ss"));
            ImGui.PopStyleColor();

            if (showCharacter)
            {
                ImGui.TableNextColumn();
                Text(ctx, ev, ctx.LabelFor(ev.ContentId), ctx.Theme.TextPrimary);
            }

            ImGui.TableNextColumn();
            // An excluded row says so in the category cell, where the button already is,
            // instead of relying on the greyed-out colour alone.
            var category = ev.Suspect
                ? $"{CategoryLabel(ev.Category)} ({Strings.TimelineExcludedRow})"
                : CategoryLabel(ev.Category);
            Text(ctx, ev, category, ctx.Theme.TextAccent);
            if (!string.IsNullOrEmpty(ev.Note) && ImGui.IsItemHovered())
                ImGui.SetTooltip(ev.Note);

            ImGui.TableNextColumn();
            Text(ctx, ev, WalletLabel(ev), ctx.Theme.TextPrimary);

            ImGui.TableNextColumn();
            Text(ctx, ev, $"{ev.Amount:+#,0;-#,0;0}",
                ev.Amount >= 0 ? ctx.Theme.PositiveDelta : ctx.Theme.NegativeDelta);

            if (!showActions) continue;

            ImGui.TableNextColumn();
            // The index keeps the button ids apart; two rows can otherwise be identical.
            var label = ev.Suspect ? Strings.TimelineRestore : Strings.TimelineExclude;
            if (ImGui.SmallButton($"{label}##{idPrefix}{i}")) pressed = ev;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(ev.Suspect ? Strings.TimelineRestoreHelp : Strings.TimelineExcludeHelp);
        }

        ImGui.EndTable();

        if (pressed is { } target) ctx.ToggleExcluded!(target);
    }

    /// <summary>
    /// One cell of text, greyed out when the record is excluded. The colour is the
    /// secondary cue - the word in the category column is the one that survives a theme.
    /// </summary>
    private static void Text(DashboardContext ctx, GilEvent ev, string text, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ev.Suspect ? ctx.Theme.TextMuted : color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static string WalletLabel(GilEvent ev) => ev.Wallet.Kind switch
    {
        GilDelta.Wallet.WalletKind.Self             => Strings.WalletSelf,
        GilDelta.Wallet.WalletKind.Retainer         => $"{Strings.WalletRetainerPrefix}{ev.Wallet.Identifier}",
        GilDelta.Wallet.WalletKind.FreeCompanyChest => Strings.WalletFcChest,
        _                                           => ev.Wallet.Kind.ToString(),
    };

    /// <summary>
    /// Shared by every view that names a category: with a private copy per tab, a member
    /// added to the enum only reaches the ones somebody remembered.
    /// </summary>
    public static string CategoryLabel(GilEventCategory c) => c switch
    {
        GilEventCategory.SubmarineReturn         => Strings.CategorySubmarineReturn,
        GilEventCategory.FreeCompanyChestDeposit => Strings.CategoryFreeCompanyChestDeposit,
        GilEventCategory.RetainerSale            => Strings.CategoryRetainerSale,
        GilEventCategory.RetainerWithdraw        => Strings.CategoryRetainerWithdraw,
        GilEventCategory.RetainerDeposit         => Strings.CategoryRetainerDeposit,
        GilEventCategory.NpcShopBuy              => Strings.CategoryNpcShopBuy,
        GilEventCategory.NpcShopSell             => Strings.CategoryNpcShopSell,
        GilEventCategory.MarketBoardBuy          => Strings.CategoryMarketBoardBuy,
        GilEventCategory.Repair                  => Strings.CategoryRepair,
        GilEventCategory.Teleport                => Strings.CategoryTeleport,
        GilEventCategory.MailReceive             => Strings.CategoryMailReceive,
        GilEventCategory.MailSend                => Strings.CategoryMailSend,
        GilEventCategory.CharacterTransfer       => Strings.CategoryCharacterTransfer,
        _                                        => Strings.CategoryMisc,
    };
}
