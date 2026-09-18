using Microsoft.AspNetCore.Mvc;
using Wayroo.Payments.BusinessLogic.Managers;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.API.Controllers;

/// <summary>
/// Opens a store's Adyen accounts, and sends the seller to Adyen to verify themselves.
/// </summary>
/// <remarks>
/// <para>
/// Names the provider, unlike the account endpoints. Creating accounts is not provider-neutral and
/// never will be — ProPay has no legal entities and Adyen has no sub-merchant accounts — so a caller
/// reaching here is asking for Adyen specifically rather than for "whatever this store is on".
/// </para>
/// <para>
/// In a host that does not talk to Adyen these endpoints still exist and answer honestly, refusing as
/// an unsupported provider. That is deliberate: an endpoint that is present in some environments and
/// absent in others is worse to integrate against than one that is always there and sometimes says no.
/// </para>
/// </remarks>
[ApiController]
[Route(Routes.AdyenOnboardingRoute)]
[ApiVersion(Routes.MinimumSupportedVersionString)]
public class AdyenOnboardingController(IAdyenOnboardingManager onboardingManager) : ControllerBase
{
    /// <summary>
    /// Opens whichever of the store's Adyen accounts do not exist yet, and reports where onboarding
    /// now stands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Safe to call again.</b> Each step is skipped if its identifier is already recorded, so a
    /// request that timed out halfway, a caller that retried, or a seller who pressed the button twice
    /// all converge on one set of accounts rather than a second set or a stuck store. A fully
    /// onboarded store makes no calls to Adyen at all.
    /// </para>
    /// <para>
    /// Answering <c>200</c> does not mean the store can be paid. It means every object Adyen needs
    /// now exists and its capabilities have been requested; whether they are granted depends on the
    /// seller finishing verification, which arrives later over webhooks.
    /// </para>
    /// </remarks>
    /// <param name="tenantId">The tenant the store sells for. Decides the industry, the currency and the merchant account.</param>
    /// <param name="storeId">The store to onboard.</param>
    /// <param name="seller">Who the seller is — the little Adyen needs to open a legal entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("", Name = nameof(OnboardStore))]
    [ProducesResponseType(typeof(AdyenAccount), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public async Task<ActionResult<AdyenAccount>> OnboardStore(
        [FromRoute] long tenantId,
        [FromRoute] long storeId,
        [FromBody] AdyenSellerDetails seller,
        CancellationToken cancellationToken)
        => Ok(await onboardingManager.Onboard(tenantId, storeId, seller, cancellationToken));

    /// <summary>
    /// Redirects the seller to Adyen's hosted onboarding page to complete their verification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A redirect rather than a payload of the URL, so the link is never handled by anything that
    /// does not need it. It authenticates the seller into their own onboarding session, and a link
    /// that reaches a log or a browser history is a link someone else can use.
    /// </para>
    /// <para>
    /// A fresh link is minted per visit because they expire, which is also why nothing stores one.
    /// </para>
    /// </remarks>
    /// <param name="tenantId">The tenant the store sells for.</param>
    /// <param name="storeId">The store whose seller is onboarding.</param>
    /// <param name="redirectUrl">Where Adyen returns the seller when they finish. Optional.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <c>302</c> to Adyen, or <c>404</c> when the store has no legal entity yet — meaning
    /// onboarding has not been run for it, which is a different thing from it having failed.
    /// </returns>
    [HttpGet("link", Name = nameof(GetOnboardingLink))]
    [ProducesResponseType(302)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public async Task<IActionResult> GetOnboardingLink(
        [FromRoute] long tenantId,
        [FromRoute] long storeId,
        [FromQuery] string? redirectUrl,
        CancellationToken cancellationToken)
    {
        var link = await onboardingManager.GetOnboardingLink(storeId, redirectUrl, cancellationToken);

        return link is null
            ? NotFound()
            // Not permanent: the link expires, and the next visit must come back here for a new one.
            : Redirect(link.ToString());
    }
}
