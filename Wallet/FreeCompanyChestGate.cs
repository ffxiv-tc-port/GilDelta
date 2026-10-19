using System;
using System.Collections.Generic;

namespace GilDelta.Wallet;

/// <summary>Why a free company chest reading is, or is not, a real balance.</summary>
public enum ChestFigureState
{
    /// <summary>The figure is the server's. Safe to diff against.</summary>
    Readable,

    /// <summary>The gil container itself is not loaded, so there is nothing to read.</summary>
    ContainerNotLoaded,

    /// <summary>
    /// The container answered a flat <c>0</c> and the server has not sent this company's
    /// balance at any point this session, so the 0 is the "no data" placeholder rather
    /// than an empty chest.
    /// </summary>
    FigureNotSentYet,
}

/// <summary>One free company chest reading: the balance, or why there isn't one.</summary>
public readonly record struct ChestFigure(long? Amount, ChestFigureState State)
{
    public bool IsKnown => Amount.HasValue;
}

/// <summary>
/// Decides whether a free company chest figure is a balance or a placeholder. 🔴 A 0 is
/// trustworthy once the server has proved it will speak about this company, and a
/// non-zero figure is that proof. So a 0 is reported as unknown until a real figure has
/// been seen for the same company in the same session, and from then on it is reported
/// as the genuine 0 it is.
/// </summary>
public sealed class FreeCompanyChestGate
{
    /// <summary>
    /// Companies whose balance the server has actually sent this session. Keyed by name
    /// because that is the identity the rest of the plugin gives a chest wallet, so a
    /// character in two companies cannot lend its trust to the other one.
    /// </summary>
    private readonly HashSet<string> _figureSeen = new(StringComparer.Ordinal);

    /// <summary>How many companies currently count as proven. Diagnostics and tests.</summary>
    public int ProvenCount => _figureSeen.Count;

    /// <summary>Whether the server has sent this company's balance this session.</summary>
    public bool HasSeenFigure(string freeCompanyName) => _figureSeen.Contains(freeCompanyName);

    /// <summary>
    /// Forgets which companies are proven. The next 0 from each of them is untrusted
    /// again, which is what a character swap requires.
    /// </summary>
    public void Reset() => _figureSeen.Clear();

    /// <param name="freeCompanyName">
    /// The company the figure belongs to, as the chest wallet is identified elsewhere.
    /// </param>
    /// <param name="containerLoaded">
    /// The gil container's <c>IsLoaded</c> flag. Kept as a gate even though it has never
    /// been observed false: it is free, it is read off the same object as the figure, and
    /// dropping it would widen what we trust on the strength of an absence of evidence.
    /// </param>
    /// <param name="rawGil">Whatever <c>GetFreeCompanyGil()</c> answered.</param>
    public ChestFigure Read(string freeCompanyName, bool containerLoaded, long rawGil)
    {
        if (!containerLoaded)
            return new ChestFigure(null, ChestFigureState.ContainerNotLoaded);

        if (rawGil != 0)
        {
            // The server has spoken about this company. Both this figure and any later 0
            // from it are real.
            _figureSeen.Add(freeCompanyName);
            return new ChestFigure(rawGil, ChestFigureState.Readable);
        }

        return _figureSeen.Contains(freeCompanyName)
            ? new ChestFigure(0, ChestFigureState.Readable)
            : new ChestFigure(null, ChestFigureState.FigureNotSentYet);
    }
}
