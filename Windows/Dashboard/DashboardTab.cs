namespace GilDelta.Windows.Dashboard;

/// <summary>
/// 🔴 Persisted as an <b>integer</b> through <see cref="Configuration.DefaultTab"/>, so
/// new members go on the END. Inserting one in the middle would silently move every
/// existing user's saved default tab to whatever now sits at that ordinal.
/// </summary>
public enum DashboardTab
{
    Timeline,
    Chart,
    Breakdown,
    Heatmap,
    Daily,
    Balances,
}
