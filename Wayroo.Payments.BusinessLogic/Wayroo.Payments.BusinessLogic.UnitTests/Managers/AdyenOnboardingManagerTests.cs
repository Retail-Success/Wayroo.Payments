using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Wayroo.Payments.BusinessLogic.Gateways.Adyen;
using Wayroo.Payments.BusinessLogic.Managers;
using Wayroo.Payments.BusinessLogic.UnitTests.TestDoubles;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.UnitTests.Managers;

/// <summary>
/// The onboarding ladder: four calls to Adyen that must happen in order, with nothing spanning them
/// and nothing to roll back to.
/// </summary>
/// <remarks>
/// <para>
/// What these tests are really about is what happens when onboarding does <i>not</i> run cleanly from
/// start to finish — which, across thousands of sellers, is the case that decides whether this works.
/// A request that times out halfway, a process that dies between two calls, a seller who presses the
/// button twice: all of them have to converge on one set of accounts.
/// </para>
/// <para>
/// The repository is a fake rather than a mock because that convergence is a property of what the
/// last write left behind.
/// </para>
/// </remarks>
public class AdyenOnboardingManagerTests
{
    private const long TenantId = 4;
    private const long StoreId = 31610;
    private const string LegalEntityId = "LE3293Q22322865PZFSX9BMK4";
    private const string BusinessLineId = "SE322KT223222H5PZCLTWFPMR";
    private const string AccountHolderId = "AH32CLR22322CJ5PZCLTWDM36";
    private const string BalanceAccountId = "BA329BG22322CJ5PZCLWPCK9R";

    private readonly InMemoryAdyenAccountRepository _repository = new();
    private readonly Mock<IAdyenOnboardingGateway> _gateway = new();
    private readonly RecordingLogger<AdyenOnboardingManager> _logger = new();
    private readonly AdyenGatewayOptions _options = new()
    {
        LegalEntityApiKey = "lem-key",
        BalancePlatformApiKey = "bcl-key",
        BalancePlatformId = "RetailSuccess",
        Tenants =
        {
            ["4"] = new AdyenTenantOptions { IndustryCode = "5944", SalesChannels = ["pos"], CurrencyCode = "USD" },
        },
    };

    public AdyenOnboardingManagerTests()
    {
        _gateway
            .Setup(g => g.CreateLegalEntity(It.IsAny<AdyenLegalEntityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LegalEntityId);
        _gateway
            .Setup(g => g.CreateBusinessLine(It.IsAny<AdyenBusinessLineRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BusinessLineId);
        _gateway
            .Setup(g => g.CreateAccountHolder(It.IsAny<AdyenAccountHolderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountHolderId);
        _gateway
            .Setup(g => g.CreateBalanceAccount(It.IsAny<AdyenBalanceAccountRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BalanceAccountId);
    }

    private AdyenOnboardingManager Manager() => new(
        _repository,
        _gateway.Object,
        Options.Create(_options),
        _logger);

    private Task<AdyenAccount> Onboard() => Manager().Onboard(
        TenantId,
        StoreId,
        new AdyenSellerDetails
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            ResidentialCountry = "US",
        },
        CancellationToken.None);

    [Fact]
    public async Task Onboard_WalksEveryRungForAStoreThatHasNeverOnboarded()
    {
        var account = await Onboard();

        account.LegalEntityId.Should().Be(LegalEntityId);
        account.BusinessLineId.Should().Be(BusinessLineId);
        account.AccountHolderId.Should().Be(AccountHolderId);
        account.BalanceAccountId.Should().Be(BalanceAccountId);
        account.OnboardingStep.Should().Be(AdyenOnboardingStep.Complete);
    }

    /// <summary>
    /// Each identifier is written down as it arrives, not batched to the end. The window between
    /// Adyen creating something and this recording it is the only place onboarding can lose track of
    /// what exists — and for a legal entity, losing track is permanent.
    /// </summary>
    [Fact]
    public async Task Onboard_RecordsEachIdentifierAsItArrives()
    {
        await Onboard();

        _repository.WrittenSteps.Should().Equal(
            AdyenOnboardingStep.LegalEntityCreated,
            AdyenOnboardingStep.BusinessLineCreated,
            AdyenOnboardingStep.AccountHolderCreated,
            AdyenOnboardingStep.Complete);
    }

    /// <summary>
    /// The property that makes onboarding safe to call again: a finished store touches Adyen not at
    /// all, and answers with the accounts it already has.
    /// </summary>
    [Fact]
    public async Task Onboard_MakesNoCallsAtAllForAStoreThatIsAlreadyComplete()
    {
        var first = await Onboard();
        _gateway.Invocations.Clear();

        var second = await Onboard();

        _gateway.Invocations.Should().BeEmpty();
        second.LegalEntityId.Should().Be(first.LegalEntityId);
        second.BusinessLineId.Should().Be(first.BusinessLineId);
        second.AccountHolderId.Should().Be(first.AccountHolderId);
        second.BalanceAccountId.Should().Be(first.BalanceAccountId);
    }

    /// <summary>
    /// Resuming, which is the case the ticket is really about: a process that died after two rungs
    /// leaves a store that has to be finished, not started again.
    /// </summary>
    [Fact]
    public async Task Onboard_ResumesFromWhereAPartFinishedAttemptStopped()
    {
        _repository.Given(new AdyenAccount
        {
            StoreId = StoreId,
            TenantId = TenantId,
            LegalEntityId = LegalEntityId,
            BusinessLineId = BusinessLineId,
            OnboardingStep = AdyenOnboardingStep.BusinessLineCreated,
        });

        var account = await Onboard();

        _gateway.Verify(
            g => g.CreateLegalEntity(It.IsAny<AdyenLegalEntityRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _gateway.Verify(
            g => g.CreateBusinessLine(It.IsAny<AdyenBusinessLineRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _gateway.Verify(
            g => g.CreateAccountHolder(It.IsAny<AdyenAccountHolderRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);

        account.OnboardingStep.Should().Be(AdyenOnboardingStep.Complete);
    }

    /// <summary>
    /// The check that runs before anything is created. A legal entity opened for a tenant whose
    /// settings are missing could never be finished — and could never be deleted either, because
    /// Adyen offers no way to remove one.
    /// </summary>
    [Fact]
    public async Task Onboard_RefusesATenantWithNoSettingsBeforeCallingAdyenAtAll()
    {
        var onboard = async () => await Manager().Onboard(
            99,
            StoreId,
            new AdyenSellerDetails { FirstName = "Ada", LastName = "Lovelace", ResidentialCountry = "US" },
            CancellationToken.None);

        await onboard.Should().ThrowAsync<InvalidOperationException>();

        _gateway.Invocations.Should().BeEmpty();
        (await _repository.GetAdyenAccount(StoreId, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Onboard_TakesTheIndustryAndChannelsFromTheTenant()
    {
        AdyenBusinessLineRequest? sent = null;
        _gateway
            .Setup(g => g.CreateBusinessLine(It.IsAny<AdyenBusinessLineRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AdyenBusinessLineRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(BusinessLineId);

        await Onboard();

        sent!.IndustryCode.Should().Be("5944");
        sent.SalesChannels.Should().Equal("pos");
        sent.LegalEntityId.Should().Be(LegalEntityId);
    }

    /// <summary>
    /// A balance account's currency is fixed for its lifetime, so it comes from the tenant's settings
    /// rather than from Adyen's default — which is EUR, even on a US platform.
    /// </summary>
    [Fact]
    public async Task Onboard_TakesTheCurrencyFromTheTenant()
    {
        AdyenBalanceAccountRequest? sent = null;
        _gateway
            .Setup(g => g.CreateBalanceAccount(It.IsAny<AdyenBalanceAccountRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AdyenBalanceAccountRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(BalanceAccountId);

        await Onboard();

        sent!.CurrencyCode.Should().Be("USD");
        sent.AccountHolderId.Should().Be(AccountHolderId);
    }

    /// <summary>
    /// Two callers onboarding one store both reach Adyen, but only the first recording of an
    /// identifier is kept. The loser continues from what the winner wrote, so both describe the same
    /// accounts rather than one of them describing accounts nothing else knows about.
    /// </summary>
    [Fact]
    public async Task Onboard_ContinuesFromTheWinnersIdentifiersAfterLosingARace()
    {
        _repository.ConflictWith = new AdyenAccount
        {
            StoreId = StoreId,
            TenantId = TenantId,
            LegalEntityId = "LE_WRITTEN_BY_THE_WINNER",
            OnboardingStep = AdyenOnboardingStep.LegalEntityCreated,
        };

        var account = await Onboard();

        account.LegalEntityId.Should().Be("LE_WRITTEN_BY_THE_WINNER");
        account.OnboardingStep.Should().Be(AdyenOnboardingStep.Complete);
    }

    /// <summary>
    /// The one failure with no way back. Adyen ignores the idempotency header on legal entities,
    /// offers no way to search for one by anything we set, and offers no way to delete one — so the
    /// log line is the only record that the orphan exists.
    /// </summary>
    [Fact]
    public async Task Onboard_ReportsALegalEntityItCreatedAndCouldNotRecord()
    {
        _repository.ConflictWith = new AdyenAccount
        {
            StoreId = StoreId,
            TenantId = TenantId,
            LegalEntityId = "LE_WRITTEN_BY_THE_WINNER",
            OnboardingStep = AdyenOnboardingStep.LegalEntityCreated,
        };

        await Onboard();

        _logger.HasSignal(PaymentsLogSignals.AdyenLegalEntityOrphaned).Should().BeTrue();
        _logger.EntryForSignal(PaymentsLogSignals.AdyenLegalEntityOrphaned)
            .Message.Should().Contain(LegalEntityId);
    }

    /// <summary>
    /// The tenant decides which merchant account a sale is processed through and which balance
    /// account is liable for it, and it is written into references that cannot be changed. Two parts
    /// of the platform disagreeing about who a seller sells for must not be resolved by guessing.
    /// </summary>
    [Fact]
    public async Task Onboard_RefusesAStoreRecordedAgainstADifferentTenant()
    {
        _repository.Given(new AdyenAccount
        {
            StoreId = StoreId,
            TenantId = 5,
            LegalEntityId = LegalEntityId,
            OnboardingStep = AdyenOnboardingStep.LegalEntityCreated,
        });

        var onboard = async () => await Onboard();

        await onboard.Should().ThrowAsync<InvalidOperationException>();
        _gateway.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOnboardingLink_AnswersWithNothingForAStoreThatHasNotOnboarded()
    {
        var link = await Manager().GetOnboardingLink(StoreId, redirectUrl: null, CancellationToken.None);

        link.Should().BeNull();
        _gateway.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOnboardingLink_MintsALinkForTheStoresLegalEntity()
    {
        await Onboard();

        AdyenOnboardingLinkRequest? sent = null;
        _gateway
            .Setup(g => g.CreateOnboardingLink(It.IsAny<AdyenOnboardingLinkRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AdyenOnboardingLinkRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(new Uri("https://onboarding.adyen.com/session/abc123"));

        var link = await Manager().GetOnboardingLink(
            StoreId,
            "https://wayroo.com/done",
            CancellationToken.None);

        link.Should().Be(new Uri("https://onboarding.adyen.com/session/abc123"));
        sent!.LegalEntityId.Should().Be(LegalEntityId);
        sent.RedirectUrl.Should().Be("https://wayroo.com/done");
    }
}
