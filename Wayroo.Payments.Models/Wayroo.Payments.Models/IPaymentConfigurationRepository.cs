namespace Wayroo.Payments.Models;

/// <summary>
/// Persistence for <see cref="PaymentProviderConfiguration"/> records.
/// </summary>
public interface IPaymentConfigurationRepository
{
    /// <summary>
    /// Inserts or overwrites the configuration for a tenant + provider.
    /// </summary>
    Task<PaymentProviderConfiguration> UpsertConfiguration(
        PaymentProviderConfiguration configuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the configuration for a tenant + provider, or <c>null</c> if none exists.
    /// </summary>
    Task<PaymentProviderConfiguration?> GetConfiguration(
        string tenantId,
        string provider,
        CancellationToken cancellationToken);
}
