using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using GilDelta.Events;

namespace GilDelta.Ipc;

/// <summary>
/// GilDelta's provider side of the fleet IPC surface.
/// </summary>
/// <remarks>
/// 🔴 A registered func runs on the <em>calling</em> plugin's thread, not the
/// framework thread: everything reachable from here must be safe for that, and
/// <see cref="GilHintStore.Submit"/> only touches concurrent collections.
/// 🔴 The endpoint name and its signature are a contract with other plugins. If the
/// shape ever has to change, register a <em>new</em> name and let this one disappear
/// — a same-name type change can succeed silently with garbage.
/// </remarks>
public sealed class GilDeltaIpc : IDisposable
{
    /// <summary>
    /// <c>bool Hint(string category, string note, int ttlMs)</c> — tells GilDelta what
    /// the next matching gil change is. Returns true when the hint was accepted.
    /// <c>category</c> is a <see cref="GilEventCategory"/> name (case-insensitive;
    /// unknown values are rejected), <c>note</c> is free text by convention prefixed
    /// with the calling plugin's own name — Dalamud does not tell us who called — and
    /// <c>ttlMs</c> is capped at <see cref="GilHintStore.MaxTtlMs"/> and must be positive.
    /// </summary>
    public const string HintEndpoint = "GilDelta.Hint";

    private readonly ICallGateProvider<string, string, int, bool> _hint;

    public GilDeltaIpc(IDalamudPluginInterface pi, GilHintStore hints)
    {
        ArgumentNullException.ThrowIfNull(pi);
        ArgumentNullException.ThrowIfNull(hints);

        _hint = pi.GetIpcProvider<string, string, int, bool>(HintEndpoint);
        _hint.RegisterFunc(hints.Submit);
    }

    public void Dispose()
    {
        // Unregistering during teardown must never be the thing that stops the rest
        // of Dispose from running.
        try
        {
            _hint.UnregisterFunc();
        }
        catch
        {
            // ignored
        }
    }
}
