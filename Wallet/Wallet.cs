namespace GilDelta.Wallet;

/// <summary>
/// One wallet and what it last read.
/// 🔴 <see cref="Amount"/> is nullable on purpose: <c>null</c> means <b>this wallet
/// could not be read</b>, which is a different claim from "this wallet holds 0".
/// </summary>
public readonly record struct Wallet(WalletId Id, long? Amount)
{
    /// <summary>False when this reading is "unknown" rather than a real balance.</summary>
    public bool IsKnown => Amount.HasValue;
}
