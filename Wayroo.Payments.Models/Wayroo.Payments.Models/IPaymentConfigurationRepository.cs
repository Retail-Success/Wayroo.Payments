namespace Wayroo.Payments.Models;

/// <summary>
/// Persistence for <see cref="PaymentProviderConfiguration"/> records.
/// </summary>
public interface IPaymentConfigurationRepository
{
    /// <summary>
    /// Inserts or overwrites the configuration for a store + provider.
    /// </summary>
    Task<PaymentProviderConfiguration> UpsertConfiguration(
        PaymentProviderConfiguration configuration,
        CancellationToken cancellationToken);

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
