using System;

namespace GilDelta.Events;

/// <summary>
/// A classification hint another plugin handed us over IPC. It is a promise about the
/// <em>next</em> matching wallet change - hence <see cref="ExpiresAt"/>: a hint whose
/// gil change never arrives must not mislabel something that happens minutes later.
/// </summary>
/// <param name="Category">The category the hint claims the next matching change is.</param>
/// <param name="Note">
/// Free text from the submitting plugin. By convention it starts with the source
/// plugin's own name (<c>"Lifestream:傳送 → grid"</c>), because Dalamud IPC does not
/// tell the provider who called it — the note is the only place the origin can live.
/// </param>
/// <param name="ExpiresAt">Absolute time after which the hint is discarded unclaimed.</param>
public readonly record struct GilHint(
    GilEventCategory Category,
    string Note,
    DateTimeOffset ExpiresAt);
