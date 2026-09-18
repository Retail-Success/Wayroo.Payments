namespace Wayroo.Payments.BusinessLogic.Gateways.Adyen;

/// <summary>
/// The Adyen settings that differ per tenant rather than per environment.
/// </summary>
/// <remarks>
/// <para>
/// Adyen's balance platform is flat: the tenant a seller sells for is not recorded anywhere in
/// Adyen's own model, so the platform has to carry the mapping itself. These are the values that
/// mapping consists of — which merchant account a sale is processed through, which balance account is
/// liable for it, and what the tenant's sellers actually sell.
/// </para>
/// <para>
/// <b>Onboarding a store for a tenant with no settings must fail rather than fall back.</b>
/// Every value here ends up written onto an object that cannot be changed afterwards, or onto money
/// in flight — an assumed industry code would mean a business line verified for the wrong trade, and
/// an assumed currency cannot be corrected once a balance account holds it.
/// </para>
/// </remarks>
public class AdyenTenantOptions
{
    /// <summary>
    /// The channel a seller starts out selling through, when the tenant names none.
    /// </summary>
    /// <remarks>
    /// Point of sale, because it is the channel that can be verified from identity and business-line
    /// checks alone. Selling online additionally needs the seller's website content verified, which
    /// is not something a new store has yet.
    /// </remarks>
    public const string DefaultSalesChannel = "pos";

    /// <summary>The currency a balance account holds when the tenant names none.</summary>
    public const string DefaultCurrencyCode = "USD";

    /// <summary>
    /// Adyen's code for the industry this tenant's sellers trade in.
    /// </summary>
    /// <remarks>
    /// Required, with no default: it is part of what Adyen verifies a business line against, so a
    /// guess would either fail verification or pass it for the wrong trade.
    /// </remarks>
    public string IndustryCode { get; set; } = string.Empty;

    /// <summary>
    /// The channels this tenant's sellers start out selling through.
    /// </summary>
    public IReadOnlyList<string> SalesChannels { get; set; } = [DefaultSalesChannel];

    /// <summary>
    /// The currency this tenant's sellers hold their earnings in.
    /// </summary>
    /// <remarks>
    /// Defaulted rather than required, because the default is right for every tenant on the
    /// platform today and the alternative — Adyen's own default — is EUR on a US platform.
    /// </remarks>
    public string CurrencyCode { get; set; } = DefaultCurrencyCode;

    /// <summary>
    /// The merchant account this tenant's sales are processed through.
    /// </summary>
    /// <remarks>
    /// Not used while onboarding — an account holder can be created without naming one, which was
    /// verified against the live API. It is recorded here because it is the same mapping, and because
    /// the code that creates payment sessions needs it.
    /// </remarks>
    public string? MerchantAccount { get; set; }

    /// <summary>
    /// The balance account that is liable for this tenant's sales.
    /// </summary>
    /// <remarks>
    /// Anything a sale does not explicitly split lands here. A missing split instruction does not
    /// error — it routes a seller's money to the platform, and it is found at reconciliation.
    /// </remarks>
    public string? LiableBalanceAccountId { get; set; }
}
