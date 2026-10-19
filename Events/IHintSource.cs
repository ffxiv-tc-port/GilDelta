using GilDelta.Wallet;

namespace GilDelta.Events;

/// <summary>
/// The hint store as the inference rules see it.
/// </summary>
/// <remarks>
/// 🔴 <see cref="TryConsume"/> mutates: a hint that matches is removed, so one hint
/// can never label two wallet changes. That is exactly why
/// <see cref="GameContext.Empty"/> hands out no source at all — replaying history
/// must not be able to eat hints meant for live gil.
/// </remarks>
public interface IHintSource
{
    /// <summary>
    /// Takes the oldest unexpired hint whose scope covers <paramref name="diff"/>,
    /// removing it. Framework thread only.
    /// </summary>
    bool TryConsume(WalletDiff diff, out GilHint hint);
}
