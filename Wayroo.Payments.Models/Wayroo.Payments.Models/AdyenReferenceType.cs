namespace Wayroo.Payments.Models;

/// <summary>
/// The kind of Adyen object a reference points at, carried as the reference's trailing two-letter
/// segment.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Store"/> is the default: a reference with no trailing type segment is a store, which is
/// what every reference written before types existed looks like.
/// </para>
/// <para>
/// The codes are part of the persisted reference, so they are fixed strings rather than something
/// derived from the member name — renaming a member must not change what a reference reads as.
/// </para>
/// </remarks>
public enum AdyenReferenceType
{
    // There is deliberately no business line. BusinessLineInfo has no reference field at all, so a
    // code for one would name something that could never be written; a business line is found only
    // through the identifier recorded against the store that owns it.

    /// <summary>A seller's store within a DSO's merchant account. The default when no type segment is present.</summary>
    Store = 0,

    /// <summary>A legal entity — who someone legally is. One per person, one per DSO.</summary>
    LegalEntity,

    /// <summary>An account holder — what a party is permitted to do. Holds capabilities.</summary>
    AccountHolder,

    /// <summary>A balance account — where money sits.</summary>
    BalanceAccount,

    /// <summary>A transfer instrument — a bank account.</summary>
    /// <remarks>
    /// Adyen's create-transfer-instrument request accepts no reference field, so this code cannot
    /// currently be written to a live object. It is here so a reference carrying it still parses.
    /// </remarks>
    TransferInstrument,

    /// <summary>A split configuration profile, attached to a merchant account.</summary>
    SplitConfiguration,

    /// <summary>A merchant account — a processing and risk boundary. Holds no funds.</summary>
    MerchantAccount,

    /// <summary>A payout sweep from a balance account to a bank account.</summary>
    /// <remarks>
    /// Sweep references are capped at 80 characters by Adyen, tighter than other objects. Not yet in
    /// the agreed type list, which stops at <see cref="MerchantAccount"/> — kept here because sweeps
    /// do accept a reference, so one may be read even though nothing writes one.
    /// </remarks>
    Sweep,
}
