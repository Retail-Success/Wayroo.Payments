using Wayroo.Payments.Models;

namespace Wayroo.Payments.SDK.Clients;

/// <summary>
/// In-process client surface for the Wayroo Payments service. Mirrors the shape used by
/// <c>Wayroo.Notification.SDK.Clients.IClient</c> so consumers (e.g. Luci.Management.Api) follow the
/// same SDK pattern across the Wayroo product suite.
/// </summary>
public interface IClient
{
    /// <summary>
    /// Retrieves the configuration for a store + provider, or <c>null</c> if none exists.
    /// </summary>
    Task<PaymentProviderConfiguration?> GetConfiguration(
        long storeId,
        string providerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves every provider configuration recorded for a store (one per provider).
    /// </summary>
    Task<IReadOnlyList<PaymentProviderConfiguration>> GetConfigurationsForStore(
        long storeId,
        CancellationToken cancellationToken);
}
