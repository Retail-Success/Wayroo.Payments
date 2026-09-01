using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.Managers;

/// <summary>
/// A store's merchant account, without the caller having to know which payment provider it lives
/// with.
/// </summary>
/// <remarks>
/// Working out the provider is the whole point of this seam. A store moves from one provider to
/// another during the migration; if callers named the provider, every one of them would have to track
/// that move, which is the coupling this service exists to remove.
/// </remarks>
public interface IPaymentAccountManager
{
    /// <summary>
    /// Reads the store's spendable balance and current account standing.
    /// </summary>
    /// <param name="tenantId">The tenant that owns the store; selects the provider credentials used.</param>
    /// <param name="storeId">The store whose account to read.</param>
    /// <param name="providerId">
    /// Optional override, for support and diagnostics. Leave null to let the service decide, which is
    /// what ordinary callers should do.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The balance, or a result with <see cref="PaymentAccountBalance.AccountExists"/> <c>false</c>
    /// when the store has no merchant account — an ordinary answer, not a failure.
    /// </returns>
    /// <exception cref="Gateways.PaymentProviderNotSupportedException">
    /// <paramref name="providerId"/> names a provider with no gateway.
    /// </exception>
    /// <exception cref="Gateways.PaymentProviderAmbiguousException">
    /// The store's recorded state does not say which provider it is on.
    /// </exception>
    /// <exception cref="Gateways.PaymentProviderException">The provider refused the request.</exception>
    Task<PaymentAccountBalance> GetBalance(
        long tenantId,
        long storeId,
        string? providerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-reads the store's account information from its provider and records it.
    /// </summary>
    /// <param name="tenantId">The tenant that owns the store; selects the provider credentials used.</param>
    /// <param name="storeId">The store whose account to refresh.</param>
    /// <param name="providerId">Optional override; see <see cref="GetBalance"/>.</param>
    /// <param name="providerAccountRef">
    /// The provider's account reference, for a store this service has no record of yet. This is how a
    /// backfill seeds a store: the caller knows the reference and supplies it, rather than this
    /// service calling another Wayroo service to find it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// What was recorded, or a result with <see cref="PaymentAccountDetails.AccountExists"/>
    /// <c>false</c> when the store has no merchant account.
    /// </returns>
    /// <exception cref="Gateways.PaymentProviderNotSupportedException">
    /// <paramref name="providerId"/> names a provider with no gateway.
    /// </exception>
    /// <exception cref="Gateways.PaymentProviderAmbiguousException">
    /// The store's recorded state does not say which provider it is on.
    /// </exception>
    /// <exception cref="Gateways.PaymentProviderException">The provider refused the request.</exception>
    Task<PaymentAccountDetails> RefreshAccount(
        long tenantId,
        long storeId,
        string? providerId,
        string? providerAccountRef,
        CancellationToken cancellationToken);
}
