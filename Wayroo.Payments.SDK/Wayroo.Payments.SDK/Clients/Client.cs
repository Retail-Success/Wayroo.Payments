using Wayroo.Payments.Models;

namespace Wayroo.Payments.SDK.Clients;

/// <summary>
/// Thin in-process facade over <see cref="IPaymentConfigurationRepository"/>. Payments has no business-logic
/// layer beyond passthrough today, so the SDK delegates straight to the repository. If/when we add domain
/// rules (validation, derived state) introduce a Wayroo.Payments.BusinessLogic project and have this client
/// delegate there instead, matching Wayroo.Notification.SDK.
/// </summary>
public class Client(IPaymentConfigurationRepository repository) : IClient
{
    public Task<PaymentProviderConfiguration?> GetConfiguration(
        long storeId,
        string providerId,
        CancellationToken cancellationToken) =>
        repository.GetConfiguration(storeId, providerId, cancellationToken);

    public Task<IReadOnlyList<PaymentProviderConfiguration>> GetConfigurationsForStore(
        long storeId,
        CancellationToken cancellationToken) =>
        repository.GetConfigurationsForStore(storeId, cancellationToken);
}
