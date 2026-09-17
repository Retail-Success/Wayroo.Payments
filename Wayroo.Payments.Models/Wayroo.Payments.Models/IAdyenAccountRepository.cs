namespace Wayroo.Payments.Models;

/// <summary>
/// Persistence for a store's Adyen account record.
/// </summary>
/// <remarks>
/// Separate from <see cref="IPaymentConfigurationRepository"/> despite sharing a record, because the
/// callers are different: onboarding and capability webhooks write here, while the credential
/// recorder and the account refresh write the provider-neutral attributes beside it. Each names only
/// what it owns, which is what lets them share the record safely.
/// </remarks>
public interface IAdyenAccountRepository
{
    /// <summary>
    /// Reads a store's Adyen account, or <c>null</c> when onboarding has never run for it.
    /// </summary>
    /// <remarks>
    /// <b>Strongly consistent.</b> The onboarding ladder decides whether to call Adyen based on what
    /// comes back here, and an eventually-consistent read that misses an identifier written moments
    /// ago would create a second one at Adyen — which, for a legal entity, cannot be undone.
    /// </remarks>
    /// <param name="storeId">The store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AdyenAccount?> GetAdyenAccount(long storeId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the identifier a rung of onboarding just created, and the step now reached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written immediately after each call to Adyen rather than once at the end, because the window
    /// between Adyen creating something and this recording it is the only place the ladder can lose
    /// track of what exists.
    /// </para>
    /// <para>
    /// Refuses to overwrite an identifier that is already stored: two concurrent onboardings of one
    /// store both call Adyen, but only the first recording of each identifier is kept, and the loser
    /// re-reads and continues from what the winner wrote. Signalled by
    /// <see cref="AdyenAccountConflictException"/> rather than a return value, because a caller that
    /// ignores it proceeds on identifiers that are not the ones in use.
    /// </para>
    /// </remarks>
    /// <param name="account">The identifiers to record. Only those set are written.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AdyenAccountConflictException">An identifier being written is already recorded.</exception>
    Task<AdyenAccount> RecordOnboardingProgress(AdyenAccount account, CancellationToken cancellationToken);

    /// <summary>
    /// Records where each capability stands, and the account standing derived from them.
    /// </summary>
    /// <remarks>
    /// Returns what the record held beforehand as well as what it now holds, so a status change can
    /// be published as a transition rather than a level, without a second and racy read.
    /// </remarks>
    /// <param name="account">The capability states to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AdyenAccountWriteResult> RecordCapabilities(AdyenAccount account, CancellationToken cancellationToken);
}
