namespace Wayroo.Payments.Messages;

/// <summary>
/// One line of the fee breakdown for a settled payment: what kind of fee it was, and how much.
/// </summary>
/// <remarks>
/// The breakdown exists so the platform can prove its take-rate arithmetic against what the
/// provider actually charged, per transaction, rather than trusting a single netted total.
/// </remarks>
public sealed record SettlementFee
{
    /// <summary>
    /// What kind of fee this is. See <see cref="SettlementFeeTypes"/> for the well-known values.
    /// </summary>
    /// <remarks>
    /// A <see cref="string"/> rather than an enum on purpose: fee taxonomies are the provider's to
    /// change, and a new fee type must not turn every settlement message into a poison message for
    /// consumers on an older package version. Sum the amounts you recognise, carry the rest through
    /// to reporting, and alert on an unfamiliar type rather than failing on it.
    /// </remarks>
    public required string FeeType { get; init; }

    /// <summary>
    /// The fee amount, positive as charged. Currency matches the settlement it belongs to.
    /// </summary>
    public required Money Amount { get; init; }
}

/// <summary>
/// Well-known values for <see cref="SettlementFee.FeeType"/>, normalized across providers.
/// </summary>
/// <remarks>
/// The list is a convention, not a closed set — a provider may report a fee type that is not here.
/// See the remarks on <see cref="SettlementFee.FeeType"/> for how to handle one.
/// </remarks>
public static class SettlementFeeTypes
{
    /// <summary>The issuing bank's share, set by the card network's interchange schedule.</summary>
    public const string Interchange = "interchange";

    /// <summary>The card network's own assessment on the transaction.</summary>
    public const string Scheme = "scheme";

    /// <summary>The payment provider's margin over interchange and scheme fees.</summary>
    public const string Markup = "markup";

    /// <summary>The platform's commission — the split that books to the platform rather than the store.</summary>
    public const string Commission = "commission";

    /// <summary>A per-transaction processing or authorization fee.</summary>
    public const string Processing = "processing";

    /// <summary>Anything the provider reported that does not map to a more specific type.</summary>
    public const string Other = "other";
}
