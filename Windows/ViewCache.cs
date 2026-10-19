using System;
using System.Collections.Generic;
using GilDelta.Characters;
using GilDelta.Wallet;

namespace GilDelta.Windows;

/// <summary>
/// Everything a widget or dashboard context is built from, in a form that is cheap to
/// compare every frame. The collections are compared by reference: their owners hand out
/// a new instance whenever the contents change.
/// </summary>
/// <param name="Scope">Whose records the view counts; null means the whole account.</param>
/// <param name="Minute">
/// The local minute the context was built in. Part of the key so "today" rolls over at
/// midnight and the context's <c>Now</c> is never more than a minute old.
/// </param>
public readonly record struct ViewInputs(
    long EventsVersion,
    long WalletsVersion,
    long LedgerVersion,
    ulong ActiveContentId,
    ulong? Scope,
    bool ExcludeFreeCompanyChest,
    IReadOnlyDictionary<ulong, string>? Labels,
    IReadOnlyList<CharacterInfo>? Characters,
    long Minute,
    TimeSpan Offset)
{
    public static long MinuteOf(DateTimeOffset now) => now.Ticks / TimeSpan.TicksPerMinute;
}

/// <summary>
/// One built context plus the inputs it was built from, so it is rebuilt when one of them
/// changes rather than every frame. Draw thread only, and it holds managed values only.
/// </summary>
public sealed class ViewCache<T> where T : class
{
    private ViewInputs _inputs;
    private IReadOnlyList<Wallet.Wallet>? _reading;
    private T? _value;
    private bool _hasValue;

    /// <param name="reading">
    /// This frame's wallet reading. Compared by contents when the instance differs: the
    /// watcher keeps the same list while no balance moves, but a caller need not rely on it.
    /// </param>
    public bool TryGet(in ViewInputs inputs, IReadOnlyList<Wallet.Wallet>? reading, out T? value)
    {
        if (_hasValue && inputs == _inputs && SameReading(reading, _reading))
        {
            value = _value;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Remembers a freshly built context. Null is a valid value: "nothing to show yet".</summary>
    public T? Store(in ViewInputs inputs, IReadOnlyList<Wallet.Wallet>? reading, T? value)
    {
        _inputs = inputs;
        _reading = reading;
        _value = value;
        _hasValue = true;
        return value;
    }

    private static bool SameReading(IReadOnlyList<Wallet.Wallet>? a, IReadOnlyList<Wallet.Wallet>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return WalletSnapshotStore.SameContents(a, b);
    }
}
