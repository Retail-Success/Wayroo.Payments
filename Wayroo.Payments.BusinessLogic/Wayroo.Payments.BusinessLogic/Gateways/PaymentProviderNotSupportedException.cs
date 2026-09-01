namespace Wayroo.Payments.BusinessLogic.Gateways;

/// <summary>
/// The caller asked for a payment provider this service has no gateway for.
/// </summary>
/// <remarks>
/// A bad request rather than a conflict: the fault is in what was asked for, and the caller can fix it
/// by asking for a supported provider or — better — by not naming one at all and letting the service
/// resolve it. Distinct from <see cref="PaymentProviderAmbiguousException"/>, where the request is
/// fine and the stored state is what needs settling.
/// </remarks>
public class PaymentProviderNotSupportedException : Exception
{
    /// <summary>
    /// Creates an exception naming the unsupported provider and what is on offer instead.
    /// </summary>
    /// <param name="requestedProviderId">The provider the caller asked for.</param>
    /// <param name="knownProviderIds">The providers a gateway is registered for.</param>
    public PaymentProviderNotSupportedException(
        string requestedProviderId,
        IReadOnlyCollection<string> knownProviderIds)
        : base($"No payment account gateway is configured for provider '{requestedProviderId}'. "
               + $"Known providers: {string.Join(", ", knownProviderIds)}.")
    {
        RequestedProviderId = requestedProviderId;
        KnownProviderIds = knownProviderIds;
    }

    /// <summary>The provider the caller asked for.</summary>
    public string RequestedProviderId { get; }

    /// <summary>The providers a gateway is registered for.</summary>
    public IReadOnlyCollection<string> KnownProviderIds { get; }
}
