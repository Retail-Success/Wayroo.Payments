namespace Wayroo.Payments.BusinessLogic.Gateways;

/// <summary>
/// The payment provider answered, and said no. Distinct from a transport failure, which propagates as
/// an unhandled exception and surfaces as a 500: this carries the provider's own explanation of why a
/// well-formed request was refused, and is surfaced to the caller as a 400.
/// </summary>
/// <remarks>
/// Mirrors how Luci.Orders' <c>StorePropayController</c> turns a <c>PropayException</c> into a
/// validation problem, so callers migrating off that endpoint see the same class of response.
/// </remarks>
public class PaymentProviderException : Exception
{
    /// <summary>
    /// Creates an exception describing a provider refusal.
    /// </summary>
    /// <param name="message">The provider's explanation, suitable for showing to support.</param>
    /// <param name="providerStatusCode">The provider's own code for the refusal. Opaque; never branch on it.</param>
    /// <param name="details">Any further detail the provider supplied.</param>
    public PaymentProviderException(string message, string? providerStatusCode = null, string? details = null)
        : base(message)
    {
        ProviderStatusCode = providerStatusCode;
        Details = details;
    }

    /// <summary>The provider's own code for the refusal. Opaque; carried so support can quote it back.</summary>
    public string? ProviderStatusCode { get; }

    /// <summary>Any further detail the provider supplied alongside the message.</summary>
    public string? Details { get; }
}
