namespace Wayroo.Payments.BusinessLogic.Gateways.Adyen;

/// <summary>
/// Creates the Adyen objects a store needs before it can be paid: the legal entity, the business
/// line, the account holder and the balance account — and the link that sends the seller to Adyen to
/// finish verifying themselves.
/// </summary>
/// <remarks>
/// <para>
/// Each method is one call to Adyen and returns the identifier it created, so the caller can record
/// it before doing anything else. Nothing here reads or writes this service's own store, and nothing
/// here decides what to call next: <b>sequencing and persistence belong to the manager</b>, because
/// the order the rungs run in and the point at which each identifier becomes durable is the whole
/// substance of making onboarding resumable.
/// </para>
/// <para>
/// Separate from <see cref="IPaymentAccountGateway"/> deliberately. That seam answers "what does this
/// store's account look like" for whichever provider it lives with, and is what keeps callers from
/// naming a provider. Creating accounts is not provider-neutral — ProPay has no legal entities and
/// Adyen has no sub-merchant accounts — so a common interface over the two would be a shape neither
/// provider actually has.
/// </para>
/// <para>
/// A refusal from Adyen surfaces as <see cref="PaymentProviderException"/>: a well-formed request the
/// provider declined, which no retry will fix. Transport failures propagate untouched, because those
/// a caller can usefully retry.
/// </para>
/// </remarks>
public interface IAdyenOnboardingGateway
{
    /// <summary>
    /// Creates the seller's legal entity.
    /// </summary>
    /// <remarks>
    /// <b>This call has no idempotency protection and cannot be undone.</b> Legal Entity Management
    /// ignores the idempotency header — the same request sent twice creates two legal entities — and
    /// Adyen offers no way to delete one. It also offers no way to search for one by anything we set,
    /// so a legal entity created and not recorded is unreachable afterwards. The caller's recorded
    /// state is the only thing standing between a retry and a duplicate.
    /// </remarks>
    /// <param name="request">Who the seller is.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The legal entity's identifier.</returns>
    /// <exception cref="PaymentProviderException">Adyen refused the request.</exception>
    Task<string> CreateLegalEntity(AdyenLegalEntityRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the seller's business line for one tenant.
    /// </summary>
    /// <remarks>
    /// Also unprotected by an idempotency key, for the same reason as the legal entity — but a
    /// duplicate business line can be deleted, so a stray one is a tidy-up rather than a permanent
    /// mark.
    /// </remarks>
    /// <param name="request">What the seller sells, and for whom.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The business line's identifier.</returns>
    /// <exception cref="PaymentProviderException">Adyen refused the request.</exception>
    Task<string> CreateBusinessLine(AdyenBusinessLineRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the seller's account holder, with every capability the platform needs requested.
    /// </summary>
    /// <remarks>
    /// Sent with an idempotency key, which the balance platform honours: a retry of the same attempt
    /// returns the account holder the first call created rather than a second one. The capabilities
    /// come back requested but not yet permitted — Adyen reports them becoming usable later, over
    /// webhooks, so nothing here records them.
    /// </remarks>
    /// <param name="request">The legal entity to act for, and which attempt this is.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The account holder's identifier.</returns>
    /// <exception cref="PaymentProviderException">Adyen refused the request.</exception>
    Task<string> CreateAccountHolder(AdyenAccountHolderRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the balance account the store's earnings land in.
    /// </summary>
    /// <remarks>
    /// A fresh account holder has no balance account of its own, so this is a separate and required
    /// call rather than something the platform derives. Also idempotency-keyed.
    /// </remarks>
    /// <param name="request">The account holder to open it under, and the currency it holds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The balance account's identifier.</returns>
    /// <exception cref="PaymentProviderException">Adyen refused the request.</exception>
    Task<string> CreateBalanceAccount(AdyenBalanceAccountRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Mints a link to Adyen's hosted onboarding page for a seller to complete verification.
    /// </summary>
    /// <remarks>
    /// The link is what keeps identity documents and Social Security numbers out of this platform
    /// entirely — the seller hands them to Adyen. It expires, so it is minted when the seller is
    /// about to be sent there and never stored.
    /// </remarks>
    /// <param name="request">The legal entity to onboard, and how to present the page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Where to send the seller.</returns>
    /// <exception cref="PaymentProviderException">Adyen refused the request.</exception>
    Task<Uri> CreateOnboardingLink(AdyenOnboardingLinkRequest request, CancellationToken cancellationToken);
}
