using System.Diagnostics.CodeAnalysis;
using GilDelta.Wallet;

namespace GilDelta.Events.Rules;

/// <summary>
/// The FC chest gained gil.
/// </summary>
/// <remarks>
/// 🔴 A submarine payout is only claimed when a plugin that actually knows about it
/// says so — a <c>SubmarineReturn</c> hint over <c>GilDelta.Hint</c>, consumed upstream
/// by <see cref="HintRule"/>. What is left is recorded as the only thing we can
/// genuinely observe: the chest went up. Calling every increase a payout would misfile
/// any other member's deposit as a submarine return.
/// </remarks>
public sealed class FreeCompanyChestDepositRule : IInferenceRule
{
    public bool TryClassify(WalletDiff diff, GameContext ctx, [NotNullWhen(true)] out GilEvent? ev)
    {
        ev = null;
        if (diff.Id.Kind != WalletKind.FreeCompanyChest) return false;
        if (diff.Delta <= 0) return false;

        ev = new GilEvent(
            Timestamp: diff.At,
            Wallet:    diff.Id,
            Amount:    diff.Delta,
            Category:  GilEventCategory.FreeCompanyChestDeposit,
            Note:      $"rule=FreeCompanyChestDepositRule; fc_chest +{diff.Delta}");
        return true;
    }
}
