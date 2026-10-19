using System.Collections.Generic;
using GilDelta.Events;
using GilDelta.Events.Rules;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events.Rules;

public class TeleportRuleTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static GameContext With(params string[] addons) =>
        new(new HashSet<string>(addons), Array.Empty<WalletDiff>(), T0);

    private static WalletDiff Fee() =>
        new(new WalletId(WalletKind.Self, ""), 1_000_000, 999_975, T0);

    [Fact]
    public void Self_spend_with_the_Teleport_window_open_classifies_as_Teleport()
    {
        var ok = new TeleportRule().TryClassify(Fee(), With("Teleport"), out var ev);

        Assert.True(ok);
        Assert.Equal(GilEventCategory.Teleport, ev!.Category);
        Assert.Equal(-25, ev.Amount);
    }

    [Fact]
    public void The_aethernet_window_alone_does_not_match()
    {
        // Aethernet travel is free, so gil moving while TelepotTown was open is
        // evidence of something else. It is watched for the breadcrumb only.
        Assert.False(new TeleportRule().TryClassify(Fee(), With("TelepotTown"), out _));
    }

    [Fact]
    public void A_gain_does_not_match()
    {
        var gain = new WalletDiff(new WalletId(WalletKind.Self, ""), 0, 500, T0);
        Assert.False(new TeleportRule().TryClassify(gain, With("Teleport"), out _));
    }

    [Fact]
    public void A_retainer_spend_does_not_match()
    {
        var diff = new WalletDiff(new WalletId(WalletKind.Retainer, "Yui"), 500, 0, T0);
        Assert.False(new TeleportRule().TryClassify(diff, With("Teleport"), out _));
    }

    [Fact]
    public void No_addon_means_no_match()
    {
        Assert.False(new TeleportRule().TryClassify(Fee(), GameContext.Empty(T0), out _));
    }

    [Fact]
    public void Every_other_rule_gets_first_refusal()
    {
        // The rule's whole defence against false positives is its position. A repair
        // paid for while the teleport menu was still inside the recency window has to
        // stay a repair.
        var inferrer = new Inferrer(new IInferenceRule[]
        {
            new RepairRule(),
            new TeleportRule(),
            new MiscRule(),
        });

        var ev = inferrer.Classify(Fee(), With("Teleport", "Repair"));

        Assert.Equal(GilEventCategory.Repair, ev.Category);
    }

    /// <summary>Both windows in the recency window, each last seen that many seconds ago.</summary>
    private static GameContext Aged(double teleportAgo, double editorAgo) =>
        new(new HashSet<string> { "Teleport", "LetterEditor" }, Array.Empty<WalletDiff>(), T0,
            addonLastSeen: new Dictionary<string, DateTimeOffset>
            {
                ["Teleport"]     = T0.AddSeconds(-teleportAgo),
                ["LetterEditor"] = T0.AddSeconds(-editorAgo),
            });

    [Fact]
    public void A_letter_written_after_the_menu_closed_is_not_a_teleport()
    {
        Assert.False(new TeleportRule().TryClassify(Fee(), Aged(teleportAgo: 8, editorAgo: 0), out _));
    }

    [Fact]
    public void A_teleport_right_after_a_letter_stays_a_teleport()
    {
        // The reverse order: the letter editor is the stale window here.
        Assert.True(new TeleportRule().TryClassify(Fee(), Aged(teleportAgo: 6, editorAgo: 9), out _));
    }

    [Fact]
    public void Without_ages_or_on_a_tie_position_still_decides()
    {
        Assert.True(new TeleportRule().TryClassify(Fee(), With("Teleport", "LetterEditor"), out _));
        Assert.True(new TeleportRule().TryClassify(Fee(), Aged(teleportAgo: 3, editorAgo: 3), out _));
    }

    [Fact]
    public void A_letter_being_read_does_not_take_a_spend_from_the_teleport()
    {
        // LetterViewer explains gains only; a spend next to it is still the teleport.
        var ctx = new GameContext(new HashSet<string> { "Teleport", "LetterViewer" },
            Array.Empty<WalletDiff>(), T0,
            addonLastSeen: new Dictionary<string, DateTimeOffset>
            {
                ["Teleport"]     = T0.AddSeconds(-8),
                ["LetterViewer"] = T0,
            });

        Assert.True(new TeleportRule().TryClassify(Fee(), ctx, out _));
    }
}
