namespace Wayroo.Payments.Models;

/// <summary>
/// A store's merchant account information as most recently read from the payment provider, and
/// recorded against the store's payment configuration. Returned by the account refresh endpoint that
/// backfills payment accounts.
/// </summary>
/// <remarks>
/// This carries account <i>standing and shape</i> — status, tier, limits — not live balances. The
/// spendable balance is <see cref="PaymentAccountBalance"/>, which is read from a different provider
/// operation. Payout capability likewise is not available here: ProPay's account-detail operation
/// does not report it, so <see cref="PaymentAccountBalance.CanReceivePayouts"/> is sourced from the
/// balance call instead.
/// </remarks>
public class PaymentAccountDetails
{
    /// <summary>
    /// Whether a merchant account could be resolved for the store at this provider. When
    /// <c>false</c> every other value is unset.
    /// </summary>
    public bool AccountExists { get; set; }

    /// <summary>The provider the account lives with (e.g. <c>propay</c>). Treat as opaque.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>The provider's reference for the merchant account, opaque to consumers.</summary>
    public string? ProviderAccountRef { get; set; }

    /// <summary>The account's standing. Non-null whenever <see cref="AccountExists"/> is <c>true</c>.</summary>
    public PaymentAccountStatus? Status { get; set; }

    /// <summary>
    /// The provider's own status vocabulary, verbatim (e.g. ProPay's <c>ReadyToProcess</c> or
    /// <c>OFACHold</c>).
    /// </summary>
    /// <remarks>
    /// <b>Diagnostic only.</b> Carried so ops can see exactly what the provider said and quote it
    /// back to them. Never branch on it — branch on <see cref="Status"/>.
    /// </remarks>
    public string? ProviderStatusCode { get; set; }

    /// <summary>Whether the store can take payments right now.</summary>
    public bool CanProcessPayments { get; set; }

    /// <summary>Whether the provider reports the account as ready for API use.</summary>
    public bool ApiReady { get; set; }

    /// <summary>The provider's account tier or product name, opaque (ProPay: e.g. <c>PPZ_NCR</c>).</summary>
    public string? Tier { get; set; }

    /// <summary>The provider's affiliation/partner grouping the account sits under, opaque.</summary>
    public string? Affiliation { get; set; }

    /// <summary>The ISO 4217 alpha-3 currency the account settles in.</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>When the merchant signed up with the provider.</summary>
    public DateTimeOffset? SignupDate { get; set; }

    /// <summary>When the account expires, for providers that expire accounts.</summary>
    public DateTimeOffset? Expiration { get; set; }

    /// <summary>Funds the provider is holding back as a risk reserve.</summary>
    public MoneyAmount? ReserveBalance { get; set; }

    /// <summary>The largest single card transaction the account may take.</summary>
    public MoneyAmount? CardTransactionLimit { get; set; }

    /// <summary>The card volume the account may take in a month.</summary>
    public MoneyAmount? CardMonthlyLimit { get; set; }

    /// <summary>The largest single ACH transaction the account may take.</summary>
    public MoneyAmount? AchTransactionLimit { get; set; }

    /// <summary>The ACH volume the account may take in a month.</summary>
    public MoneyAmount? AchMonthlyLimit { get; set; }

    /// <summary>When this information was last read from the provider.</summary>
    public DateTimeOffset? RefreshedOn { get; set; }
}
