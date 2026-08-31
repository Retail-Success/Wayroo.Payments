namespace Wayroo.Payments.Models;

/// <summary>
/// A store's provider-neutral merchant account balance and current standing. The replacement for the
/// ProPay-shaped balance response served by Luci.Orders'
/// <c>StorePropayController.GetPropayAccountBalanceAsync</c>.
/// </summary>
/// <remarks>
/// Balances are read live from the provider on every request; <see cref="Status"/> and the two
/// capability flags come from the account detail recorded for the store, refreshed on demand when
/// none has been recorded yet.
/// </remarks>
public class PaymentAccountBalance
{
    /// <summary>
    /// Whether a merchant account could be resolved for the store at this provider. When
    /// <c>false</c> every other value is unset — this is not an error, and the endpoint still
    /// returns 200.
    /// </summary>
    public bool AccountExists { get; set; }

    /// <summary>The provider the account lives with (e.g. <c>propay</c>). Treat as opaque.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// The provider's reference for the merchant account, opaque to consumers. For ProPay this is
    /// the account number.
    /// </summary>
    public string? ProviderAccountRef { get; set; }

    /// <summary>Funds settled and available to spend or pay out right now.</summary>
    public MoneyAmount? AvailableBalance { get; set; }

    /// <summary>Funds captured but not yet settled, still inside the provider's hold period.</summary>
    public MoneyAmount? PendingBalance { get; set; }

    /// <summary>Funds the provider is holding back as a risk reserve.</summary>
    public MoneyAmount? ReserveBalance { get; set; }

    /// <summary>
    /// The account's standing. Non-null whenever <see cref="AccountExists"/> is <c>true</c> —
    /// downstream consumers depend on that (Luci.Integrations.Api casts it unconditionally). Check
    /// <see cref="StatusIsProvisional"/> before acting on it.
    /// </summary>
    public PaymentAccountStatus? Status { get; set; }

    /// <summary>
    /// Whether <see cref="Status"/> is a placeholder rather than the account's real standing.
    /// </summary>
    /// <remarks>
    /// The balance is read live and is always real. Standing is not on the provider's balance call,
    /// so for a store whose standing has never been recorded it takes a second provider read — and
    /// when only that second read fails, the balance is still served rather than the whole request
    /// failing over a value the caller may not even use. <c>true</c> says exactly that happened:
    /// <see cref="Status"/> is <see cref="PaymentAccountStatus.Pending"/> and
    /// <see cref="CanProcessPayments"/> is <c>false</c> because nothing is known, not because the
    /// provider said so. A caller that gates selling on this should treat it as "unknown, retry"
    /// rather than "no". Never <c>true</c> when <see cref="AccountExists"/> is <c>false</c>.
    /// </remarks>
    public bool StatusIsProvisional { get; set; }

    /// <summary>
    /// Whether the store can take payments right now. This is the field that gates selling — not
    /// <see cref="Status"/>, which can say <see cref="PaymentAccountStatus.ActionRequired"/> while
    /// the provider is still happy to process during a grace period.
    /// </summary>
    public bool CanProcessPayments { get; set; }

    /// <summary>
    /// Whether funds can currently be paid out to the merchant's bank. Independent of
    /// <see cref="CanProcessPayments"/>: an account can keep taking money while payouts are held.
    /// </summary>
    public bool CanReceivePayouts { get; set; }
}
