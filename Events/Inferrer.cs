using System;
using System.Collections.Generic;
using GilDelta.Wallet;

namespace GilDelta.Events;

public sealed class Inferrer
{
    private readonly IReadOnlyList<IInferenceRule> _rules;
    private readonly Action<Exception, string>? _onRuleFault;

    /// <summary>
    /// Rule types already reported. Framework thread only, like <see cref="Classify"/>;
    /// a broken rule throws on every wallet change, so it gets exactly one line.
    /// </summary>
    private readonly HashSet<Type> _reportedFaults = new();

    /// <param name="onRuleFault">
    /// (exception, message) the first time a rule type throws. Optional: Plugin.cs
    /// wires it to a Warning, tests pass their own, a bare chain needs none.
    /// </param>
    public Inferrer(IReadOnlyList<IInferenceRule> rules, Action<Exception, string>? onRuleFault = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Count == 0)
            throw new ArgumentException("Inferrer must have at least one rule.", nameof(rules));
        _rules = rules;
        _onRuleFault = onRuleFault;
    }

    public GilEvent Classify(WalletDiff diff, GameContext ctx)
    {
        foreach (var rule in _rules)
        {
            try
            {
                if (rule.TryClassify(diff, ctx, out var ev))
                    return ev;
            }
            catch (Exception ex)
            {
                // Still falls through to the next rule - one broken rule may not stop
                // the others - and the fault is reported, once per rule type.
                ReportFault(rule, ex);
            }
        }

        // Reachable only when no rule matched (the chain forgot a fallback).
        // MiscRule as the last rule guarantees this branch is dead in practice;
        // we still produce a well-formed GilEvent so callers never see null.
        return new GilEvent(diff.At, diff.Id, diff.Delta,
            GilEventCategory.Misc, "rule=<unmatched>; no rule matched");
    }

    private void ReportFault(IInferenceRule rule, Exception ex)
    {
        var faulted = rule.GetType();
        if (!_reportedFaults.Add(faulted))
            return;

        _onRuleFault?.Invoke(ex,
            $"GilDelta: inference rule {faulted.Name} threw and was skipped ({ex.Message}); " +
            "the events it would have classified now fall through to the next rule.");
    }
}
