using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using GilDelta.Localization;

namespace GilDelta.Windows.Dashboard;

/// <summary>
/// The records behind whichever figure was last clicked on the dashboard.
/// A window of its own rather than a popup: a popup dies the moment the user clicks
/// anything, and a window stays put so two days can be compared without reopening.
/// 🔴 It rebuilds from the live context every frame instead of holding the row list it
/// was opened with. The exclude button lives in here, and a snapshot would go on showing
/// a record as counted after the user had just thrown it out.
/// </summary>
public sealed class DetailWindow : Window
{
    private readonly Func<DashboardContext?> _ctxProvider;
    private DetailQuery? _query;

    public DetailWindow(Func<DashboardContext?> ctxProvider)
        : base($"GilDelta {Strings.DetailTitle}##gildelta-detail", ImGuiWindowFlags.None)
    {
        _ctxProvider = ctxProvider;

        Size = new Vector2(600, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 200),
            MaximumSize = new Vector2(1600, 1200),
        };
    }

    /// <summary>Called from a tab's draw, i.e. already on the draw thread.</summary>
    public void Show(DetailQuery query)
    {
        _query = query;

        // Raised only when it was closed. Pulling it in front on every click would drag
        // it over the dashboard the user is still clicking cells on.
        if (IsOpen) return;
        IsOpen = true;
        BringToFront();
    }

    public override void Draw()
    {
        if (_query is not { } query)
        {
            ImGui.TextDisabled(Strings.DetailNothingPicked);
            return;
        }

        var ctx = _ctxProvider();
        if (ctx is null)
        {
            ImGui.TextDisabled(Strings.WaitingForGameState);
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextPrimary);
        ImGui.TextUnformatted(query.Title);
        ImGui.PopStyleColor();

        if (ctx.ExcludeFreeCompanyChest)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Strings.ExcludingFcChestNow);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.ExcludeFcChestHelp);
        }

        var result = DetailSelection.Build(
            ctx.Events, ctx.Excluded, DetailSelection.Refresh(ctx, query));

        DrawTotalLine(ctx, result);
        DrawReconciliation(ctx, result);

        // "How much of the list am I actually looking at" belongs on the row: a list that
        // silently stops at 500 reads as a list that ends there.
        if (result.IsTruncated)
            ImGui.TextDisabled($"{Strings.DetailRowsShown}: {result.Rows.Count} / {result.MatchedCount}");

        ImGui.Separator();

        if (result.Rows.Count == 0)
        {
            ImGui.TextDisabled(Strings.DetailNoRecords);
            return;
        }

        EventTable.Draw(ctx, result.Rows, "gddetail", scrollY: true);
    }

    private static void DrawTotalLine(DashboardContext ctx, DetailResult result)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
        ImGui.TextUnformatted($"{Strings.DetailSum}:");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text,
            result.Total >= 0 ? ctx.Theme.PositiveDelta : ctx.Theme.NegativeDelta);
        ImGui.TextUnformatted($"{result.Total:+#,0;-#,0;0}");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.TextDisabled($"({Strings.DailyColumnEvents}: {result.CountedCount})");

        if (result.ExcludedCount == 0) return;

        // How many records are being left out belongs on the row, not in a tooltip: the
        // total right next to it depends on it.
        ImGui.SameLine();
        ImGui.TextDisabled($"+{result.ExcludedCount} {Strings.TimelineExcludedCount}");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.TimelineExcludeHelp);
    }

    /// <summary>
    /// Says out loud whether the rows below add up to the figure that was clicked.
    /// 🔴 The interesting case is a settled day whose events the retention setting has
    /// already deleted: the day still stands, the detail is empty or short, and without
    /// this line the user is left staring at two numbers that disagree with no way to
    /// know that is expected.
    /// </summary>
    private static void DrawReconciliation(DashboardContext ctx, DetailResult result)
    {
        if (result.Difference is not { } difference) return;

        if (difference == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextMuted);
            ImGui.TextUnformatted(Strings.DetailReconciles);
            ImGui.PopStyleColor();
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.TextAccent);
        ImGui.TextUnformatted($"{Strings.DetailDifference}: {difference:+#,0;-#,0;0}");
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(result.Query.SummaryFromLedger
                ? Strings.DetailDifferenceLedgerHelp
                : Strings.DetailDifferenceHelp);
    }
}
