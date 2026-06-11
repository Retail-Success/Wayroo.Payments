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
}
