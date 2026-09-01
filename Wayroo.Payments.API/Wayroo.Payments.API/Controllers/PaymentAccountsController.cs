using Microsoft.AspNetCore.Mvc;
using Wayroo.Payments.BusinessLogic.Managers;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.API.Controllers;

/// <summary>
/// A store's merchant account at whichever payment provider it transacts through: its spendable
/// balance, and the account information this service records about it.
/// </summary>
/// <remarks>
/// <para>
/// The provider-neutral replacement for the ProPay-shaped reads Luci.Orders serves at
/// <c>/tenants/{tenantId}/stores/{storeId}/propay/account-balance</c>. Callers branch on
/// <see cref="PaymentAccountBalance.Status"/> and the capability flags, never on a provider's own
/// vocabulary — and never name the provider, which is
/// <see cref="IPaymentAccountManager"/>'s job to work out.
/// </para>
/// <para>
/// Tenant-scoped because every provider call selects its credentials by tenant — see
/// <see cref="Routes.AccountsRoute"/>.
/// </para>
/// </remarks>
[ApiController]
[Route(Routes.AccountsRoute)]
[ApiVersion(Routes.MinimumSupportedVersionString)]
public class PaymentAccountsController(IPaymentAccountManager accountManager) : ControllerBase
{
    /// <summary>
    /// Retrieves the store's spendable balance and current account standing, read live from its
    /// payment provider.
    /// </summary>
    /// <remarks>
    /// A store with no merchant account is answered with <c>200</c> and <c>accountExists: false</c>,
    /// not a <c>404</c> — "this store never onboarded" is an ordinary answer that callers act on, and
    /// the Orders endpoint this replaces behaves the same way.
    /// </remarks>
    /// <param name="tenantId">The tenant that owns the store.</param>
    /// <param name="storeId">The store identifier.</param>
    /// <param name="providerId">
    /// Optional. Forces a specific provider (e.g. <c>propay</c>) instead of letting the service
    /// decide, for support and diagnostics. Ordinary callers should omit it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("balance", Name = nameof(GetAccountBalance))]
    [ProducesResponseType(typeof(PaymentAccountBalance), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 409)]
    public async Task<ActionResult<PaymentAccountBalance>> GetAccountBalance(
        [FromRoute] long tenantId,
        [FromRoute] long storeId,
        [FromQuery] string? providerId,
        CancellationToken cancellationToken)
        => Ok(await accountManager.GetBalance(tenantId, storeId, providerId, cancellationToken));

    /// <summary>
    /// Re-reads the store's account information from its payment provider and records it, then
    /// returns what was recorded. The per-store unit of work a payment-account backfill drives.
    /// </summary>
    /// <remarks>
    /// Writes only the account-information attributes, so it never disturbs the provider credentials
    /// recorded by the configuration recorder. Safe to repeat: the call is idempotent apart from the
    /// refresh timestamp. A store this service holds no account reference for is answered with
    /// <c>accountExists: false</c> unless the caller supplies one.
    /// </remarks>
    /// <param name="tenantId">The tenant that owns the store.</param>
    /// <param name="storeId">The store identifier.</param>
    /// <param name="providerId">Optional; see <see cref="GetAccountBalance"/>.</param>
    /// <param name="request">
    /// Optional. Supplies the provider's account reference for a store this service holds no record
    /// for, which is how a backfill seeds one.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("refresh", Name = nameof(RefreshAccount))]
    [ProducesResponseType(typeof(PaymentAccountDetails), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 409)]
    public async Task<ActionResult<PaymentAccountDetails>> RefreshAccount(
        [FromRoute] long tenantId,
        [FromRoute] long storeId,
        [FromQuery] string? providerId,
        [FromBody] RefreshPaymentAccountRequest? request,
        CancellationToken cancellationToken)
        => Ok(await accountManager.RefreshAccount(
            tenantId,
            storeId,
            providerId,
            request?.ProviderAccountRef,
            cancellationToken));
}
