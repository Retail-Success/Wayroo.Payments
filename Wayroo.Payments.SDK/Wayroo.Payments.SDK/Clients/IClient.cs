using Refit;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.SDK.Clients;

/// <summary>
/// Typed HTTP client for the Wayroo Payments microservice. Refit generates the implementation at runtime
/// from these annotations; consumers inject <see cref="IClient"/> directly.
/// </summary>
/// <remarks>
/// The micro is unauthenticated (matches the Wayroo.Notification convention) and trusts the calling
/// composite to enforce authorization. StoreId flows in as a route parameter rather than from a JWT claim.
/// </remarks>
public interface IClient
{
    /// <summary>
    /// Retrieves every provider configuration recorded for the store (one per provider).
    /// </summary>
    [Get("/api/payments/v1.0/stores/{storeId}/configurations")]
    Task<IReadOnlyList<PaymentProviderConfiguration>> GetConfigurationsForStore(
        long storeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the configuration for the store + provider. Returns 404 (which Refit surfaces as an
    /// <see cref="ApiException"/>) when no configuration has been recorded.
    /// </summary>
    [Get("/api/payments/v1.0/stores/{storeId}/configurations/{providerId}")]
    Task<PaymentProviderConfiguration> GetConfiguration(
        long storeId,
        string providerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the store's spendable balance and current account standing from the payment provider.
    /// </summary>
    /// <remarks>
    /// The provider-neutral replacement for the Orders SDK's
    /// <c>IStorePropayClient.GetPropayAccountBalanceAsync</c>. A store with no merchant account is
    /// answered with <c>200</c> and <see cref="PaymentAccountBalance.AccountExists"/> <c>false</c>,
    /// never a 404, so callers branch on the flag rather than catching. Balances are in major units
    /// (dollars, not cents) and <see cref="PaymentAccountBalance.Status"/> is never null when the
    /// account exists — but check <see cref="PaymentAccountBalance.StatusIsProvisional"/> before
    /// acting on it, which flags the case where the balance came back but the standing behind it
    /// could not be read.
    /// </remarks>
    /// <param name="tenantId">The tenant that owns the store; selects the provider credentials used.</param>
    /// <param name="storeId">The store whose account to read.</param>
    /// <param name="providerId">
    /// Optional. Forces a specific provider instead of letting the service decide, for support and
    /// diagnostics. Ordinary callers should omit it — which provider a store transacts through is the
    /// service's job to know, and leaving it out is what lets a store move providers without this
    /// call changing.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [Get("/api/payments/v1.0/tenants/{tenantId}/stores/{storeId}/account/balance")]
    Task<PaymentAccountBalance> GetAccountBalance(
        long tenantId,
        long storeId,
        string? providerId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-reads the store's account information from the provider, records it, and returns what was
    /// recorded. The per-store unit of work a payment-account backfill drives.
    /// </summary>
    /// <remarks>
    /// Safe to repeat, and it only writes the account-information attributes — the provider
    /// credentials recorded from the provider's webhooks are left untouched. A store that never
    /// onboarded comes back with <see cref="PaymentAccountDetails.AccountExists"/> <c>false</c>
    /// rather than an error, so a sweep can record that and move on.
    /// </remarks>
    /// <param name="tenantId">The tenant that owns the store; selects the provider credentials used.</param>
    /// <param name="storeId">The store whose account to refresh.</param>
    /// <param name="providerId">Optional provider override; see <see cref="GetAccountBalance"/>.</param>
    /// <param name="request">
    /// Optional. Supplies the provider's account reference for a store the service holds no record
    /// for — how a backfill seeds one. The service does not go looking for it elsewhere.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [Post("/api/payments/v1.0/tenants/{tenantId}/stores/{storeId}/account/refresh")]
    Task<PaymentAccountDetails> RefreshAccount(
        long tenantId,
        long storeId,
        [Body] RefreshPaymentAccountRequest? request = null,
        string? providerId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens whichever of the store's Adyen accounts do not exist yet, and reports where onboarding
    /// now stands.
    /// </summary>
    /// <remarks>
    /// <b>Safe to call again.</b> Each step is skipped if its identifier is already recorded, so a
    /// call that timed out halfway or a caller that retried converges on one set of accounts rather
    /// than a second set. A fully onboarded store makes no calls to Adyen at all. Success does not
    /// mean the store can be paid — it means the accounts exist and their capabilities have been
    /// requested; verification is reported later, over webhooks.
    /// </remarks>
    /// <param name="tenantId">The tenant the store sells for; decides the industry, currency and merchant account.</param>
    /// <param name="storeId">The store to onboard.</param>
    /// <param name="seller">Who the seller is — the little Adyen needs to open a legal entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [Post("/api/payments/v1.0/tenants/{tenantId}/stores/{storeId}/adyen/onboarding")]
    Task<AdyenAccount> OnboardStore(
        long tenantId,
        long storeId,
        [Body] AdyenSellerDetails seller,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the redirect to Adyen's hosted onboarding page for the store's seller.
    /// </summary>
    /// <remarks>
    /// The endpoint answers with a <c>302</c>, so a client that follows redirects will follow it to
    /// Adyen. Callers that need the destination itself should disable redirect following and read the
    /// <c>Location</c> header — and then treat it as a credential, because it authenticates the
    /// seller into their own onboarding session. A store that has not been onboarded answers
    /// <c>404</c>.
    /// </remarks>
    /// <param name="tenantId">The tenant the store sells for.</param>
    /// <param name="storeId">The store whose seller is onboarding.</param>
    /// <param name="redirectUrl">Where Adyen returns the seller when they finish. Optional.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [Get("/api/payments/v1.0/tenants/{tenantId}/stores/{storeId}/adyen/onboarding/link")]
    Task<HttpResponseMessage> GetAdyenOnboardingLink(
        long tenantId,
        long storeId,
        string? redirectUrl = null,
        CancellationToken cancellationToken = default);
}
