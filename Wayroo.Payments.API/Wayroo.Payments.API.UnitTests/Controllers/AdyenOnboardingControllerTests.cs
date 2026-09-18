using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Wayroo.Payments.API.Controllers;
using Wayroo.Payments.BusinessLogic.Managers;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.API.UnitTests.Controllers;

/// <summary>
/// The controller is HTTP plumbing only — the ladder, its resumability and the "not onboarded" answer
/// belong to <see cref="IAdyenOnboardingManager"/> and are tested against it. What is left to prove
/// here is that route values reach the manager, and that the onboarding link leaves as a redirect
/// rather than as a payload.
/// </summary>
public class AdyenOnboardingControllerTests
{
    private const long TenantId = 4;
    private const long StoreId = 31610;

    private readonly Mock<IAdyenOnboardingManager> _onboardingManager = new();
    private readonly AdyenOnboardingController _controller;

    public AdyenOnboardingControllerTests()
        => _controller = new AdyenOnboardingController(_onboardingManager.Object);

    [Fact]
    public async Task OnboardStore_ReturnsWhereOnboardingNowStands()
    {
        var seller = new AdyenSellerDetails
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            ResidentialCountry = "US",
        };

        var expected = new AdyenAccount
        {
            StoreId = StoreId,
            TenantId = TenantId,
            LegalEntityId = "LE3293Q22322865PZFSX9BMK4",
            OnboardingStep = AdyenOnboardingStep.Complete,
        };

        _onboardingManager
            .Setup(m => m.Onboard(TenantId, StoreId, seller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _controller.OnboardStore(TenantId, StoreId, seller, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeSameAs(expected);
    }

    /// <summary>
    /// A redirect, not a payload. The link authenticates the seller into their own onboarding session,
    /// so the fewer things that handle it the better.
    /// </summary>
    [Fact]
    public async Task GetOnboardingLink_RedirectsToAdyen()
    {
        _onboardingManager
            .Setup(m => m.GetOnboardingLink(StoreId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Uri("https://onboarding.adyen.com/session/abc123"));

        var result = await _controller.GetOnboardingLink(TenantId, StoreId, null, CancellationToken.None);

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;

        redirect.Url.Should().Be("https://onboarding.adyen.com/session/abc123");
        // The link expires, so the next visit has to come back here for a new one.
        redirect.Permanent.Should().BeFalse();
    }

    [Fact]
    public async Task GetOnboardingLink_PassesTheCallersReturnDestinationThrough()
    {
        _onboardingManager
            .Setup(m => m.GetOnboardingLink(StoreId, "https://wayroo.com/done", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Uri("https://onboarding.adyen.com/session/abc123"));

        var result = await _controller.GetOnboardingLink(
            TenantId,
            StoreId,
            "https://wayroo.com/done",
            CancellationToken.None);

        result.Should().BeOfType<RedirectResult>();
        _onboardingManager.VerifyAll();
    }

    /// <summary>
    /// A store nobody has onboarded is a <c>404</c>, which is a different answer from onboarding
    /// having been attempted and failed.
    /// </summary>
    [Fact]
    public async Task GetOnboardingLink_AnswersNotFoundForAStoreWithNoLegalEntity()
    {
        _onboardingManager
            .Setup(m => m.GetOnboardingLink(StoreId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Uri?)null);

        var result = await _controller.GetOnboardingLink(TenantId, StoreId, null, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }
}
