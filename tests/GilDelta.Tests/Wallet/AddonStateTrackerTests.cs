using System.Collections.Generic;
using System.Linq;
using GilDelta.Wallet;

namespace GilDelta.Tests.Wallet;

/// <summary>
/// The plugin samples open addons into one list it reuses every frame, so the tracker
/// must have taken everything it needs by the time Tick returns.
/// </summary>
public class AddonStateTrackerTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    [Fact]
    public void Every_sampled_addon_is_seen_with_one_shared_timestamp()
    {
        var tracker = new AddonStateTracker();

        tracker.Tick(new List<string> { "Shop", "Repair" });

        var seen = tracker.LastSeenWithin(Window);
        Assert.Equal(new[] { "Shop", "Repair" }, seen.Keys.ToArray());
        Assert.Equal(seen["Shop"], seen["Repair"]);
    }

    [Fact]
    public void Reusing_the_sample_list_does_not_disturb_what_was_already_seen()
    {
        var tracker = new AddonStateTracker();
        var sample = new List<string> { "Shop", "Repair" };
        tracker.Tick(sample);
        var shopAt = tracker.LastSeenWithin(Window)["Shop"];

        sample.Clear();
        sample.Add("Teleport");
        tracker.Tick(sample);

        var seen = tracker.LastSeenWithin(Window);
        Assert.Equal(new[] { "Shop", "Repair", "Teleport" }, seen.Keys.ToArray());
        Assert.Equal(shopAt, seen["Shop"]);
        Assert.True(seen["Teleport"] >= shopAt);
    }

    [Fact]
    public void An_empty_sample_changes_nothing()
    {
        var tracker = new AddonStateTracker();
        tracker.Tick(new List<string> { "Shop" });
        var before = tracker.LastSeenWithin(Window)["Shop"];

        tracker.Tick(new List<string>());

        Assert.Equal(before, tracker.LastSeenWithin(Window)["Shop"]);
        Assert.Equal(new[] { "Shop" }, tracker.RecentlyOpen(Window).ToArray());
    }
}
