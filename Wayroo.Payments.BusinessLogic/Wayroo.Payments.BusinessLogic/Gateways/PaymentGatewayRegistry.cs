namespace Wayroo.Payments.BusinessLogic.Gateways;

/// <summary>
/// Indexes the registered <see cref="IPaymentAccountGateway"/> implementations by their provider id.
/// </summary>
/// <remarks>
/// Built over the injected set rather than keyed DI so that the registry can also answer
/// "which providers do we support at all", which provider resolution needs in order to ignore a
/// recorded provider we have no gateway for.
/// </remarks>
public class PaymentGatewayRegistry : IPaymentGatewayRegistry
{
    private readonly Dictionary<string, IPaymentAccountGateway> _gatewaysByProviderId;

    /// <summary>
    /// Indexes the supplied gateways.
    /// </summary>
    /// <param name="gateways">Every registered gateway.</param>
    /// <exception cref="ArgumentException">
    /// Two gateways claim the same provider id. Left as a hard failure at construction rather than a
    /// silent last-one-wins, because which of the two answers would otherwise depend on registration
    /// order — and the two would be reaching different providers.
    /// </exception>
    public PaymentGatewayRegistry(IEnumerable<IPaymentAccountGateway> gateways)
    {
        ArgumentNullException.ThrowIfNull(gateways);

        _gatewaysByProviderId = new Dictionary<string, IPaymentAccountGateway>(StringComparer.OrdinalIgnoreCase);

        foreach (var gateway in gateways)
        {
            if (string.IsNullOrWhiteSpace(gateway.ProviderId))
            {
                throw new ArgumentException(
                    $"{gateway.GetType().Name} does not declare a {nameof(IPaymentAccountGateway.ProviderId)}.",
                    nameof(gateways));
            }

            if (!_gatewaysByProviderId.TryAdd(gateway.ProviderId, gateway))
            {
                throw new ArgumentException(
                    $"More than one payment account gateway is registered for provider '{gateway.ProviderId}'.",
                    nameof(gateways));
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> KnownProviderIds => _gatewaysByProviderId.Keys;

    /// <inheritdoc />
    public bool TryGet(string? providerId, out IPaymentAccountGateway gateway)
    {
        if (!string.IsNullOrWhiteSpace(providerId)
            && _gatewaysByProviderId.TryGetValue(providerId.Trim(), out var found))
        {
            gateway = found;
            return true;
        }

        gateway = null!;
        return false;
    }
}
