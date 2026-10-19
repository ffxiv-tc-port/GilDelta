namespace GilDelta.Events;

/// <summary>
/// 🔴 Persisted <b>by name</b> (JsonStringEnumConverter), so the ordinal positions are
/// free to move but the names are not: renaming a member orphans every row already on
/// disk that carries it. Adding a member is safe — an older build simply skips rows it
/// cannot parse.
/// </summary>
public enum GilEventCategory
{
    /// <summary>
    /// Only ever assigned from a <c>SubmarineReturn</c> hint over <c>GilDelta.Hint</c>.
    /// An FC chest going up on its own is <see cref="FreeCompanyChestDeposit"/> — it
    /// could just as easily be another member paying in.
    /// </summary>
    SubmarineReturn,

    /// <summary>Any observed increase in the FC chest with nothing claiming it.</summary>
    FreeCompanyChestDeposit,

    RetainerSale,
    RetainerWithdraw,
    RetainerDeposit,
    NpcShopBuy,
    NpcShopSell,
    MarketBoardBuy,
    Repair,
    Teleport,

    /// <summary>
    /// Gil that arrived while a moogle letter was open. Gil the account moved to
    /// itself outranks this and relabels both halves - see
    /// <see cref="CharacterTransfer"/>.
    /// </summary>
    MailReceive,

    /// <summary>Gil attached to a moogle letter being written.</summary>
    MailSend,

    Misc,

    /// <summary>
    /// Gil the account moved between two of its own characters - by mail, in
    /// practice, which is why neither half has an addon to sniff. Both halves carry
    /// it, so the pair nets to zero account-wide and is kept out of the settlement
    /// book entirely - see <see cref="DailyLedger.IsBooked"/>.
    /// </summary>
    CharacterTransfer,
}
