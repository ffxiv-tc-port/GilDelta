using System;
using Dalamud.Plugin.Services;
using System.Collections.Generic;

namespace GilDelta.Wallet;

public sealed class WalletWatcher : IDisposable
{
    private readonly IFramework _framework;
    private readonly WalletReader _reader;
    private readonly Func<bool>? _isEnabled;

    /// <summary>
    /// Holds the previous balances and decides what counts as a change. 🔴 It - not this
    /// class - is what guarantees no event is ever produced from an unreadable wallet.
    /// </summary>
    private readonly WalletBaseline _baseline = new();

    /// <summary>
    /// The latest reading. Immutable; the same instance from tick to tick while no wallet
    /// changes, so it is safe to keep and cheap to compare by reference.
    /// </summary>
    public IReadOnlyList<Wallet> LastSnapshot { get; private set; } = Array.Empty<Wallet>();
    public event Action<WalletDiff>? OnDiff;

    /// <param name="isEnabled">
    /// Gate evaluated every tick. While it returns false the baseline is dropped and
    /// nothing is emitted - that is what stops "log out of A, log in as B" from being
    /// recorded as one enormous gil swing, because B's first reading then establishes
    /// a fresh baseline instead of being diffed against A's.
    /// </param>
    public WalletWatcher(IFramework framework, WalletReader reader, Func<bool>? isEnabled = null)
    {
        _framework = framework;
        _reader = reader;
        _isEnabled = isEnabled;
        _framework.Update += Tick;
    }

    /// <summary>
    /// Forgets the current baseline. The next reading re-seeds it without emitting a
    /// diff (a wallet is only diffed once it already has a previous value).
    /// </summary>
    public void Reset()
    {
        _baseline.Clear();
        _reader.ResetSession();
        LastSnapshot = Array.Empty<Wallet>();
    }

    private void Tick(IFramework _)
    {
        try
        {
            if (_isEnabled is not null && !_isEnabled())
            {
                if (_baseline.Count > 0 || LastSnapshot.Count > 0) Reset();
                return;
            }

            var snapshot = _reader.ReadAll();
            LastSnapshot = snapshot;
            var now = DateTimeOffset.Now;

            // Every wallet goes through the baseline every tick, also when the reader handed
            // back the same list: Reset() drops the baseline without touching the reading.
            // By index: enumerating through the interface would box an enumerator per tick.
            for (var i = 0; i < snapshot.Count; i++)
            {
                if (_baseline.Observe(snapshot[i], now) is { } diff)
                    OnDiff?.Invoke(diff);
            }
        }
        catch
        {
            // Skip this tick; don't propagate exceptions out of the framework callback.
        }
    }

    public void Dispose()
    {
        _framework.Update -= Tick;
    }
}
