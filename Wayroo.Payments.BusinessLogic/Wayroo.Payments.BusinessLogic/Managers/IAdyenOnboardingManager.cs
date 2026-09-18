using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.Managers;

/// <summary>
/// Opens a store's Adyen accounts, and sends the seller to Adyen to verify themselves.
/// </summary>
/// <remarks>
/// <para>
/// Onboarding is four calls to Adyen that must happen in order, each needing the identifier the one
/// before returned, and nothing spans them transactionally. <b>There is also nothing to roll back
/// to:</b> Adyen offers no way to delete a legal entity, so a part-built account is never undone,
/// only carried forward. That is what makes this a resumable sequence rather than a transaction — and
/// why the identifier from each call is recorded the moment it arrives, before the next one is made.
/// </para>
/// <para>
/// The consequence for callers is the useful part: <b>onboarding may simply be called again.</b> A
/// request that timed out halfway, a lambda that died between two calls, a seller who pressed the
/// button twice — all of them converge on one set of accounts rather than a second set or a stuck
/// store.
/// </para>
/// </remarks>
public interface IAdyenOnboardingManager
{
    /// <summary>
    /// Opens whichever of the store's Adyen accounts do not exist yet, and returns where onboarding
    /// now stands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Safe to call repeatedly. Each step is skipped if its identifier is already recorded, so a
    /// second call on a fully onboarded store makes no calls to Adyen at all and returns the same
    /// identifiers as the first.
    /// </para>
    /// <para>
    /// Completing this does not mean the store can be paid. It means every object Adyen needs now
    /// exists and the capabilities have been requested; whether they are granted depends on the
    /// seller finishing verification, which is reported separately.
    /// </para>
    /// </remarks>
    /// <param name="tenantId">The tenant the store sells for. Decides the merchant account, the industry and the currency.</param>
    /// <param name="storeId">The store to onboard.</param>
    /// <param name="seller">Who the seller is — the little Adyen needs to open a legal entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The store's Adyen account as it now stands, including how far onboarding got.</returns>
    /// <exception cref="Gateways.PaymentProviderException">Adyen refused one of the calls.</exception>
    /// <exception cref="InvalidOperationException">
    /// The tenant has no Adyen settings recorded, in which case nothing was created at all — the
    /// check runs before the first call precisely so that it cannot leave a legal entity behind.
    /// </exception>
    Task<AdyenAccount> Onboard(
        long tenantId,
        long storeId,
        AdyenSellerDetails seller,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mints a fresh link to Adyen's hosted onboarding, for a seller to complete verification.
    /// </summary>
    /// <remarks>
    /// Minted per visit rather than stored, because the link both expires and authenticates the
    /// seller into their own session.
    /// </remarks>
    /// <param name="storeId">The store whose seller is onboarding.</param>
    /// <param name="redirectUrl">Where Adyen should return the seller when they finish. Optional.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Where to send the seller, or <c>null</c> when the store has no legal entity yet — an ordinary
    /// answer meaning onboarding has not been run for it, not a failure.
    /// </returns>
    /// <exception cref="Gateways.PaymentProviderException">Adyen refused to mint a link.</exception>
    Task<Uri?> GetOnboardingLink(long storeId, string? redirectUrl, CancellationToken cancellationToken);
}
