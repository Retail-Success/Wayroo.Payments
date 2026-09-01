namespace Wayroo.Payments.BusinessLogic.Gateways;

/// <summary>
/// The service cannot tell which payment provider a store is currently on, or was asked for one it
/// has no gateway for.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="PaymentProviderException"/>, which means a provider was reached and
/// refused the request. This one never reaches a provider: the request is well-formed and the stored
/// state is not, so it is answered as a conflict for an operator to settle rather than retried.
/// </para>
/// <para>
/// Failing here is deliberate. A store with records for two providers and nothing marking which is
/// current is precisely the state in which guessing would read a balance from — or worse, act
/// against — the wrong merchant account.
/// </para>
/// </remarks>
public class PaymentProviderAmbiguousException : Exception
{
    /// <summary>
    /// Creates an exception describing why a provider could not be settled on.
    /// </summary>
    /// <param name="message">What was ambiguous, in terms an operator can act on.</param>
    /// <param name="storeId">The store the resolution was for.</param>
    /// <param name="candidateProviderIds">The providers that were in contention, if any.</param>
    public PaymentProviderAmbiguousException(
        string message,
        long storeId,
        IReadOnlyCollection<string>? candidateProviderIds = null)
        : base(message)
    {
        StoreId = storeId;
        CandidateProviderIds = candidateProviderIds ?? [];
    }

    /// <summary>The store the resolution was for.</summary>
    public long StoreId { get; }

    /// <summary>The providers that were in contention. Empty when the request named an unknown one.</summary>
    public IReadOnlyCollection<string> CandidateProviderIds { get; }
}
