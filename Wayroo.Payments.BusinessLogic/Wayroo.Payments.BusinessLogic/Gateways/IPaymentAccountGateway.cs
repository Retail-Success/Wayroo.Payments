using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.Gateways;

/// <summary>
/// Reads a store's merchant account from the payment provider it lives with. The seam that keeps
/// provider vocabulary — ProPay account statuses, cent-denominated amounts, XML transaction types —
/// out of the managers, the controllers and the response contracts.
/// </summary>
/// <remarks>
/// <para>
/// A gateway talks to its payment provider and to this service's own store. It must not call another
/// Wayroo service: an account reference this service has not been told about is not something to go
/// hunting for, it is something to be given (see <c>providerAccountRef</c> on
/// <see cref="RefreshAccountDetails"/>).
/// </para>
/// <para>
/// Adding a second provider is a sibling implementation under <c>Gateways/{Provider}/</c> plus its own
/// <c>Add{Provider}AccountGateway()</c>. <see cref="IPaymentGatewayRegistry"/> picks it up from the
/// registered set; nothing else changes.
/// </para>
/// </remarks>
public interface IPaymentAccountGateway
{
    /// <summary>The provider this gateway speaks for, e.g. <c>propay</c>.</summary>
    string ProviderId { get; }

    /// <summary>
    /// Reads the store's spendable balance and current standing.
    /// </summary>
    /// <param name="tenantId">The tenant that owns the store; selects the provider credentials to call with.</param>
    /// <param name="storeId">The store whose account to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The balance, or <c>null</c> when this service holds no account reference for the store — which
    /// is an ordinary answer, not a failure. Seed one with <see cref="RefreshAccountDetails"/>.
    /// </returns>
    /// <exception cref="PaymentProviderException">The provider rejected the request.</exception>
    Task<PaymentAccountBalance?> GetBalance(long tenantId, long storeId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the store's account information from the provider and records it against the store's
    /// payment configuration.
    /// </summary>
    /// <param name="tenantId">The tenant that owns the store; selects the provider credentials to call with.</param>
    /// <param name="storeId">The store whose account to refresh.</param>
    /// <param name="providerAccountRef">
    /// The provider's account reference, for a store this service has no record of yet. Optional: when
    /// omitted the recorded reference is used. This is how a backfill seeds a store — the caller knows
    /// the reference, so it supplies it rather than this service fetching it from elsewhere.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The refreshed account information, or <c>null</c> when no account reference is recorded and
    /// none was supplied.
    /// </returns>
    /// <exception cref="PaymentProviderException">The provider rejected the request.</exception>
    Task<PaymentAccountDetails?> RefreshAccountDetails(
        long tenantId,
        long storeId,
        string? providerAccountRef,
        CancellationToken cancellationToken);
}
