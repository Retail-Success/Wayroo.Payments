namespace Wayroo.Payments.BusinessLogic.Gateways;

/// <summary>
/// The gateways this service can talk to, looked up by provider id.
/// </summary>
/// <remarks>
/// The dispatch seam anticipated by <c>PaymentConfigurationMessageHandler</c> in the configuration
/// recorder lambda: one place that turns an opaque provider id into the implementation that speaks
/// that provider, so neither a controller nor a message handler has to know the set.
/// </remarks>
public interface IPaymentGatewayRegistry
{
    /// <summary>Every provider id a gateway is registered for.</summary>
    IReadOnlyCollection<string> KnownProviderIds { get; }

    /// <summary>
    /// Finds the gateway for a provider.
    /// </summary>
    /// <param name="providerId">The provider id, matched case-insensitively.</param>
    /// <param name="gateway">The gateway, when one is registered.</param>
    /// <returns><c>true</c> when a gateway is registered for the provider.</returns>
    bool TryGet(string? providerId, out IPaymentAccountGateway gateway);
}
