using System.Diagnostics.CodeAnalysis;
using GilDelta.Events;
using GilDelta.Events.Rules;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events;

public class InferrerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static WalletDiff Diff() => new(new WalletId(WalletKind.Self, ""), 0, 5, T0);

    [Fact]
    public void Falls_back_to_Misc_when_no_rule_matches()
    {
        var inferrer = new Inferrer(new IInferenceRule[] { new MiscRule() });
        var diff = new WalletDiff(
            new WalletId(WalletKind.Self, ""),
            Before: 100, After: 200, At: DateTimeOffset.UnixEpoch);

        var ev = inferrer.Classify(diff, GameContext.Empty(diff.At));

        Assert.Equal(GilEventCategory.Misc, ev.Category);
        Assert.Equal(100, ev.Amount);
    }

    [Fact]
    public void First_matching_rule_wins()
    {
        var fakeAlwaysSubmarine = new FakeRule(GilEventCategory.SubmarineReturn);
        var inferrer = new Inferrer(new IInferenceRule[]
        {
            fakeAlwaysSubmarine,
            new MiscRule(),
        });

        var diff = new WalletDiff(
            new WalletId(WalletKind.Self, ""), 100, 200, DateTimeOffset.UnixEpoch);

        var ev = inferrer.Classify(diff, GameContext.Empty(diff.At));

        Assert.Equal(GilEventCategory.SubmarineReturn, ev.Category);
    }

    [Fact]
    public void Returns_unmatched_Misc_when_all_rules_decline()
    {
        // The "should be unreachable" branch in Inferrer.Classify is reachable
        // when the chain forgets a fallback. Pin its contract here.
        var inferrer = new Inferrer(new IInferenceRule[] { new AlwaysFalseRule() });
        var diff = new WalletDiff(
            new WalletId(WalletKind.Self, ""), 0, 1, DateTimeOffset.UnixEpoch);

        var ev = inferrer.Classify(diff, GameContext.Empty(diff.At));

        Assert.Equal(GilEventCategory.Misc, ev.Category);
        Assert.Contains("unmatched", ev.Note);
    }

    [Fact]
    public void Throwing_rule_is_skipped_and_chain_continues()
    {
        var inferrer = new Inferrer(new IInferenceRule[]
        {
            new ThrowingRule(),
            new MiscRule(),
        });
        var diff = new WalletDiff(
            new WalletId(WalletKind.Self, ""), 0, 5, DateTimeOffset.UnixEpoch);

        var ev = inferrer.Classify(diff, GameContext.Empty(diff.At));

        Assert.Equal(GilEventCategory.Misc, ev.Category);
        Assert.Contains("MiscRule", ev.Note);
    }

    [Fact]
    public void Throwing_rule_is_reported_once_and_still_lands_in_Misc()
    {
        var log = new RecordingLog();
        var inferrer = new Inferrer(
            new IInferenceRule[] { new ThrowingRule(), new MiscRule() }, log.Record);

        var ev = inferrer.Classify(Diff(), GameContext.Empty(T0));

        Assert.Equal(GilEventCategory.Misc, ev.Category);
        Assert.Equal(1, log.Count);
        Assert.Contains("ThrowingRule", log.LastMessage);
        Assert.Contains("rule blew up", log.LastMessage);
        Assert.IsType<InvalidOperationException>(log.LastException);
    }

    [Fact]
    public void A_rule_that_keeps_throwing_is_reported_only_once()
    {
        var log = new RecordingLog();
        var inferrer = new Inferrer(
            new IInferenceRule[] { new ThrowingRule(), new MiscRule() }, log.Record);

        inferrer.Classify(Diff(), GameContext.Empty(T0));
        inferrer.Classify(Diff(), GameContext.Empty(T0));

        Assert.Equal(1, log.Count);
    }

    [Fact]
    public void Each_throwing_rule_type_gets_its_own_line()
    {
        var log = new RecordingLog();
        var inferrer = new Inferrer(
            new IInferenceRule[] { new ThrowingRule(), new AlsoThrowingRule(), new MiscRule() },
            log.Record);

        var ev = inferrer.Classify(Diff(), GameContext.Empty(T0));

        Assert.Equal(GilEventCategory.Misc, ev.Category);
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void A_chain_that_never_throws_logs_nothing()
    {
        var log = new RecordingLog();
        var inferrer = new Inferrer(new IInferenceRule[] { new MiscRule() }, log.Record);

        var ev = inferrer.Classify(Diff(), GameContext.Empty(T0));

        Assert.Equal(GilEventCategory.Misc, ev.Category);
        Assert.Equal(0, log.Count);
    }

    private sealed class RecordingLog
    {
        public int Count { get; private set; }
        public string LastMessage { get; private set; } = "";
        public Exception? LastException { get; private set; }

        public void Record(Exception ex, string message)
        {
            Count++;
            LastMessage = message;
            LastException = ex;
        }
    }

    private sealed class FakeRule(GilEventCategory category) : IInferenceRule
    {
        public bool TryClassify(WalletDiff diff, GameContext ctx, [NotNullWhen(true)] out GilEvent? ev)
        {
            ev = new GilEvent(diff.At, diff.Id, diff.Delta, category, "fake");
            return true;
        }
    }

    private sealed class AlwaysFalseRule : IInferenceRule
    {
        public bool TryClassify(WalletDiff diff, GameContext ctx, [NotNullWhen(true)] out GilEvent? ev)
        {
            ev = null;
            return false;
        }
    }

    private sealed class ThrowingRule : IInferenceRule
    {
        public bool TryClassify(WalletDiff diff, GameContext ctx, [NotNullWhen(true)] out GilEvent? ev)
            => throw new InvalidOperationException("rule blew up");
    }

    private sealed class AlsoThrowingRule : IInferenceRule
    {
        public bool TryClassify(WalletDiff diff, GameContext ctx, [NotNullWhen(true)] out GilEvent? ev)
            => throw new NotSupportedException("other rule blew up");
    }
}
