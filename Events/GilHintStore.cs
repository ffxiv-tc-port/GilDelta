using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using GilDelta.Wallet;

namespace GilDelta.Events;

/// <summary>
/// Holds the classification hints other plugins push in over
/// <c>GilDelta.Hint</c>, and hands them to the rule chain.
/// </summary>
/// <remarks>
/// 🔴 <b>Two threads, split deliberately.</b> <see cref="Submit"/> runs on the
/// <em>calling plugin's</em> thread — Dalamud invokes an IPC provider inline on
/// whichever thread called it — so it may only touch the concurrent collections.
/// <see cref="Drain"/> and <see cref="TryConsume"/> run on the framework thread and
/// own <c>_active</c> exclusively; nothing else may ever look at that list.
/// </remarks>
public sealed class GilHintStore : IHintSource
{
    /// <summary>Longest lifetime a hint may ask for. Five minutes is already generous.</summary>
    public const int MaxTtlMs = 300_000;

    /// <summary>
    /// Submissions accepted while nothing is draining them (no character loaded, say).
    /// A consumer stuck in a loop must not be able to grow this without bound.
    /// </summary>
    public const int MaxPendingHints = 256;

    /// <summary>Hints kept waiting for a matching wallet change at any one time.</summary>
    public const int MaxActiveHints = 64;

    private readonly ConcurrentQueue<GilHint> _pending = new();
    private readonly ConcurrentDictionary<string, byte> _unknownReported = new(StringComparer.Ordinal);

    /// <summary>Framework thread only. See the remarks on the class.</summary>
    private readonly List<GilHint> _active = new();

    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string>? _log;
    private int _pendingCount;

    /// <param name="clock">Injectable so tests can age hints without sleeping.</param>
    /// <param name="log">
    /// Where the "unknown category" diagnostic goes. Information level in production:
    /// it is the line to ask a user for when a consumer plugin's hints never land.
    /// </param>
    public GilHintStore(Func<DateTimeOffset>? clock = null, Action<string>? log = null)
    {
        _clock = clock ?? (() => DateTimeOffset.Now);
        _log = log;
    }

    /// <summary>Category strings <see cref="Submit"/> accepts, for diagnostics and docs.</summary>
    public static IReadOnlyList<string> AcceptedCategories { get; } = Enum.GetNames<GilEventCategory>();

    /// <summary>Hints currently waiting for a wallet change. Framework thread only.</summary>
    public int ActiveCount => _active.Count;

    // ── Caller's thread ────────────────────────────────────────────────────────

    /// <summary>
    /// Records a hint about the next matching wallet change. Safe to call from any
    /// thread; returns false (and records nothing) for anything it does not accept.
    /// </summary>
    public bool Submit(string category, string note, int ttlMs)
    {
        if (string.IsNullOrWhiteSpace(category)) return false;
        if (ttlMs <= 0) return false;

        if (!TryParseCategory(category, out var parsed))
        {
            // Once per distinct bad value. A consumer looping on a typo would
            // otherwise write one log line per call, which is how a diagnostic
            // becomes the thing that needs diagnosing.
            if (_unknownReported.TryAdd(category, 0))
            {
                _log?.Invoke(
                    $"GilDelta: 收到未知的分類提示「{category}」，已忽略。可接受的值：{string.Join(", ", AcceptedCategories)}");
            }
            return false;
        }

        // Read-then-increment is deliberately not atomic: the cap is a safety valve,
        // not an invariant, and a handful of extra entries under a race is harmless.
        if (Volatile.Read(ref _pendingCount) >= MaxPendingHints) return false;
        Interlocked.Increment(ref _pendingCount);

        if (ttlMs > MaxTtlMs) ttlMs = MaxTtlMs;
        _pending.Enqueue(new GilHint(parsed, note ?? string.Empty,
            _clock() + TimeSpan.FromMilliseconds(ttlMs)));
        return true;
    }

    private static bool TryParseCategory(string category, out GilEventCategory parsed)
    {
        parsed = default;

        // Enum.TryParse also accepts the *numeric* form ("3" → NpcShopBuy), which
        // would turn a consumer's stray number into a confident misclassification.
        // Names only.
        var first = category[0];
        if (char.IsDigit(first) || first == '-' || first == '+') return false;

        return Enum.TryParse(category, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
    }

    // ── Framework thread ───────────────────────────────────────────────────────

    /// <summary>
    /// Moves submitted hints into the active list and drops expired ones. Cheap
    /// enough to call every tick; <see cref="TryConsume"/> calls it too, so a hint
    /// submitted moments before its gil change is never missed by one tick.
    /// </summary>
    public void Drain()
    {
        var now = _clock();

        while (_pending.TryDequeue(out var hint))
        {
            Interlocked.Decrement(ref _pendingCount);
            if (hint.ExpiresAt <= now) continue;
            _active.Add(hint);
        }

        // A plain loop rather than RemoveAll: a lambda capturing `now` would allocate on
        // every tick. Back to front, so the survivors keep their order.
        for (var i = _active.Count - 1; i >= 0; i--)
            if (_active[i].ExpiresAt <= now) _active.RemoveAt(i);

        // Oldest first: the hints least likely to still be true.
        if (_active.Count > MaxActiveHints)
            _active.RemoveRange(0, _active.Count - MaxActiveHints);
    }

    /// <inheritdoc/>
    public bool TryConsume(WalletDiff diff, out GilHint hint)
    {
        Drain();

        var now = _clock();
        for (var i = 0; i < _active.Count; i++)
        {
            var candidate = _active[i];
            if (candidate.ExpiresAt <= now) continue;
            if (!HintScope.Matches(candidate.Category, diff.Id.Kind, diff.Delta)) continue;

            _active.RemoveAt(i);
            hint = candidate;
            return true;
        }

        hint = default;
        return false;
    }
}
