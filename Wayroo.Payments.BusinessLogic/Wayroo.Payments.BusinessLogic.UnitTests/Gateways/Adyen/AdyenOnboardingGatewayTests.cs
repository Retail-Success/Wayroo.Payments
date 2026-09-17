using Adyen.BalancePlatform.Services;
using Adyen.Core.Client;
using Adyen.LegalEntityManagement.Services;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Wayroo.Payments.BusinessLogic.Gateways;
using Wayroo.Payments.BusinessLogic.Gateways.Adyen;
using Wayroo.Payments.BusinessLogic.UnitTests.TestDoubles;
using Wayroo.Payments.Models;
using BclModels = Adyen.BalancePlatform.Models;
using LemModels = Adyen.LegalEntityManagement.Models;

namespace Wayroo.Payments.BusinessLogic.UnitTests.Gateways.Adyen;

/// <summary>
/// What this service actually sends Adyen when it creates a seller's accounts.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these assertions is about something that cannot be put right afterwards. A balance
/// account's currency is fixed for its lifetime. A reference is writable only at creation. A legal
/// entity cannot be deleted at all. An idempotency key that varies between retries silently stops
/// protecting anything. None of it fails loudly at the time — it is discovered later, on a seller's
/// account, holding their money.
/// </para>
/// <para>
/// The payloads are captured rather than matched, so a failure reports what was sent rather than only
/// that nothing matched.
/// </para>
/// </remarks>
public class AdyenOnboardingGatewayTests
{
    private const long TenantId = 5;
    private const long StoreId = 31610;
    private const string LegalEntityId = "LE3293Q22322865PZFSX9BMK4";
    private const string AccountHolderId = "AH32CLR22322CJ5PZCLTWDM36";
    private const string BalancePlatformId = "RetailSuccess";

    // The worked example from the agreed convention.
    private static readonly Guid OwnerId = Guid.ParseExact("8f14e45fceea167a5a36dedd4bea2543", "N");

    private readonly Mock<ILegalEntitiesService> _legalEntities = new();
    private readonly Mock<IBusinessLinesService> _businessLines = new();
    private readonly Mock<IAccountHoldersService> _accountHolders = new();
    private readonly Mock<IBalanceAccountsService> _balanceAccounts = new();
    private readonly Mock<IHostedOnboardingService> _hostedOnboarding = new();
    private readonly RecordingLogger<AdyenOnboardingGateway> _logger = new();

    private delegate void TryDeserialize<T>(out T? result);

    private AdyenOnboardingGateway Gateway(
        AdyenReferenceEnvironment environment = AdyenReferenceEnvironment.Prod) => new(
        _legalEntities.Object,
        _businessLines.Object,
        _accountHolders.Object,
        _balanceAccounts.Object,
        _hostedOnboarding.Object,
        Options.Create(new AdyenGatewayOptions
        {
            LegalEntityApiKey = "lem-key",
            BalancePlatformApiKey = "bcl-key",
            BalancePlatformId = BalancePlatformId,
            ReferenceEnvironment = environment,
        }),
        _logger);

    /// <summary>
    /// A response that carries what Adyen created. Mocked at the response interface rather than over
    /// HTTP, because what is under test is the request this service builds and the identifier it
    /// reads back, not the library's own serialization.
    /// </summary>
    private static Mock<TResponse> Ok<TResponse, TValue>(TValue value)
        where TResponse : class, IOk<TValue?>
        where TValue : class
    {
        var response = new Mock<TResponse>();
        response
            .Setup(r => r.TryDeserializeOkResponse(out It.Ref<TValue?>.IsAny))
            .Callback(new TryDeserialize<TValue>((out TValue? result) => result = value))
            .Returns(true);

        return response;
    }

    /// <summary>
    /// A refusal: Adyen answered, and said no. Every <c>TryDeserialize</c> is left returning false
    /// except the one for the status being simulated.
    /// </summary>
    private static Mock<TResponse> Refusal<TResponse, TValue, TError>(TError error, System.Net.HttpStatusCode statusCode)
        where TResponse : class, IOk<TValue?>, IBadRequest<TError?>
        where TValue : class
    {
        var response = new Mock<TResponse>();
        response.SetupGet(r => r.StatusCode).Returns(statusCode);
        response.SetupGet(r => r.ReasonPhrase).Returns(statusCode.ToString());
        response
            .Setup(r => r.TryDeserializeOkResponse(out It.Ref<TValue?>.IsAny))
            .Returns(false);
        response
            .Setup(r => r.TryDeserializeBadRequestResponse(out It.Ref<TError?>.IsAny))
            .Callback(new TryDeserialize<TError>((out TError? result) => result = error))
            .Returns(true);

        return response;
    }

    [Fact]
    public async Task CreateLegalEntity_ReturnsTheIdentifierAdyenAssigned()
    {
        SetUpLegalEntityCreate(out _);

        var created = await Gateway().CreateLegalEntity(LegalEntityRequest(), CancellationToken.None);

        created.Should().Be(LegalEntityId);
    }

    /// <summary>
    /// The reference is the only handle we will ever have on a legal entity: Adyen cannot search by
    /// it, cannot delete the entity, and will not let us set it later. While the platform holds no
    /// person identity it names the store — a weaker claim than the object deserves, but a true one.
    /// </summary>
    [Fact]
    public async Task CreateLegalEntity_NamesTheStoreWhileThereIsNoPersonToName()
    {
        SetUpLegalEntityCreate(out var sent);

        await Gateway().CreateLegalEntity(LegalEntityRequest(), CancellationToken.None);

        sent().Reference.Should().Be("Pt000005_s0031610_le");
    }

    /// <summary>
    /// The seam the whole cross-tenant story rests on: the day the platform mints a person
    /// identifier, the same call starts writing genuinely person-scoped references, and only objects
    /// created from then on carry them.
    /// </summary>
    [Fact]
    public async Task CreateLegalEntity_UsesThePersonScopeAsSoonAsThereIsAPerson()
    {
        SetUpLegalEntityCreate(out var sent);

        await Gateway().CreateLegalEntity(
            LegalEntityRequest() with { OwnerId = OwnerId },
            CancellationToken.None);

        sent().Reference.Should().Be("Pp8f14e45fceea167a5a36dedd4bea2543_le");
    }

    /// <summary>
    /// Dev and QA share one Adyen test platform, so the environment letter is the only thing
    /// separating their objects in a single pool.
    /// </summary>
    [Theory]
    [InlineData(AdyenReferenceEnvironment.Dev, "Dt000005_s0031610_le")]
    [InlineData(AdyenReferenceEnvironment.Qa, "Qt000005_s0031610_le")]
    [InlineData(AdyenReferenceEnvironment.Prod, "Pt000005_s0031610_le")]
    public async Task CreateLegalEntity_StampsTheEnvironmentOnTheReference(
        AdyenReferenceEnvironment environment,
        string expected)
    {
        SetUpLegalEntityCreate(out var sent);

        await Gateway(environment).CreateLegalEntity(LegalEntityRequest(), CancellationToken.None);

        sent().Reference.Should().Be(expected);
    }

    /// <summary>
    /// Name and residential country, and nothing else. The rest of the seller's identity — their date
    /// of birth, their address, the Social Security number US verification turns on — is given to
    /// Adyen by the seller, and the platform's not holding it is the point of onboarding this way.
    /// </summary>
    [Fact]
    public async Task CreateLegalEntity_SendsNoMorePersonalDetailThanAdyenNeeds()
    {
        SetUpLegalEntityCreate(out var sent);

        await Gateway().CreateLegalEntity(LegalEntityRequest(), CancellationToken.None);

        var payload = sent();

        payload.Type.Should().Be(LemModels.LegalEntityInfoRequiredType.TypeEnum.Individual);
        payload.Individual!.Name!.FirstName.Should().Be("Ada");
        payload.Individual.Name.LastName.Should().Be("Lovelace");
        payload.Individual.ResidentialAddress!.Country.Should().Be("US");
        payload.Individual.BirthData.Should().BeNull();
        payload.Individual.IdentificationData.Should().BeNull();
        payload.Individual.Email.Should().BeNull();
    }

    /// <summary>
    /// Legal Entity Management ignores the idempotency header — verified against the live API, where
    /// the same request sent twice created two legal entities. Sending one anyway would invite a
    /// reader to believe this call is protected when the recorded state is the only guard there is.
    /// </summary>
    [Fact]
    public async Task CreateLegalEntity_SendsNoIdempotencyKey()
    {
        RequestOptions? options = null;
        var response = Ok<ICreateLegalEntityApiResponse, LemModels.LegalEntity>(
            new LemModels.LegalEntity { Id = LegalEntityId });

        _legalEntities
            .Setup(s => s.CreateLegalEntityAsync(
                It.IsAny<LemModels.LegalEntityInfoRequiredType>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<LemModels.LegalEntityInfoRequiredType, RequestOptions, CancellationToken>(
                (_, requestOptions, _) => options = requestOptions)
            .ReturnsAsync(response.Object);

        await Gateway().CreateLegalEntity(LegalEntityRequest(), CancellationToken.None);

        options.Should().BeNull();
    }

    /// <summary>
    /// A refusal is Adyen's own explanation of why a well-formed request was declined, and it is what
    /// support has to work from. The PSP reference travels with it because it is the first thing Adyen
    /// asks for.
    /// </summary>
    [Fact]
    public async Task CreateLegalEntity_SurfacesAdyensOwnExplanationOfARefusal()
    {
        var response = Refusal<ICreateLegalEntityApiResponse, LemModels.LegalEntity, LemModels.ServiceError>(
            new LemModels.ServiceError
            {
                ErrorCode = "30_112",
                Message = "Invalid country code.",
                PspReference = "8825abc",
            },
            System.Net.HttpStatusCode.BadRequest);

        _legalEntities
            .Setup(s => s.CreateLegalEntityAsync(
                It.IsAny<LemModels.LegalEntityInfoRequiredType>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var create = async () => await Gateway().CreateLegalEntity(LegalEntityRequest(), CancellationToken.None);

        var refusal = (await create.Should().ThrowAsync<PaymentProviderException>()).Which;

        refusal.Message.Should().Be("Invalid country code.");
        refusal.ProviderStatusCode.Should().Be("30_112");
        refusal.Details.Should().Be("8825abc");
    }

    /// <summary>
    /// A 200 with no identifier is not a success. Treating it as one would record an empty legal
    /// entity id and leave the ladder unable to tell that the rung had not run.
    /// </summary>
    [Fact]
    public async Task CreateLegalEntity_TreatsAResponseWithNoIdentifierAsARefusal()
    {
        var response = Refusal<ICreateLegalEntityApiResponse, LemModels.LegalEntity, LemModels.ServiceError>(
            new LemModels.ServiceError(),
            System.Net.HttpStatusCode.OK);

        response
            .Setup(r => r.TryDeserializeOkResponse(out It.Ref<LemModels.LegalEntity?>.IsAny))
            .Callback(new TryDeserialize<LemModels.LegalEntity>(
                (out LemModels.LegalEntity? result) => result = new LemModels.LegalEntity()))
            .Returns(true);

        _legalEntities
            .Setup(s => s.CreateLegalEntityAsync(
                It.IsAny<LemModels.LegalEntityInfoRequiredType>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var create = async () => await Gateway().CreateLegalEntity(LegalEntityRequest(), CancellationToken.None);

        await create.Should().ThrowAsync<PaymentProviderException>();
    }

    /// <summary>
    /// Business-line verification is what gates taking a payment at all, so this rung exists for one
    /// reason: to make the seller's sales channels something Adyen can verify.
    /// </summary>
    [Fact]
    public async Task CreateBusinessLine_DeclaresPaymentProcessingForTheGivenChannels()
    {
        LemModels.BusinessLineInfo? sent = null;
        var response = Ok<ICreateBusinessLineApiResponse, LemModels.BusinessLine>(
            new LemModels.BusinessLine { Id = "SE322KT223222H5PZCLTWFPMR" });

        _businessLines
            .Setup(s => s.CreateBusinessLineAsync(
                It.IsAny<LemModels.BusinessLineInfo>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<LemModels.BusinessLineInfo, RequestOptions, CancellationToken>(
                (payload, _, _) => sent = payload)
            .ReturnsAsync(response.Object);

        var created = await Gateway().CreateBusinessLine(
            new AdyenBusinessLineRequest
            {
                TenantId = TenantId,
                StoreId = StoreId,
                LegalEntityId = LegalEntityId,
                IndustryCode = "5944",
                SalesChannels = ["pos"],
            },
            CancellationToken.None);

        created.Should().Be("SE322KT223222H5PZCLTWFPMR");
        sent!.LegalEntityId.Should().Be(LegalEntityId);
        sent.Service.Should().Be(LemModels.BusinessLineInfo.ServiceEnum.PaymentProcessing);
        sent.IndustryCode.Should().Be("5944");
        sent.SalesChannels.Should().Equal("pos");

        // An empty list would ask Adyen to verify no website rather than tell it there is none to
        // verify, and website verification is what gates selling online.
        sent.WebData.Should().BeNull();
    }

    /// <summary>
    /// Every capability the platform needs, asked for in the one call that creates the account
    /// holder. <c>receiveFromPlatformPayments</c> is the one that costs money when it is missing: a
    /// split then books to the platform's own account instead of the seller's, and nothing errors.
    /// </summary>
    [Fact]
    public async Task CreateAccountHolder_RequestsEveryCapabilityThePlatformNeeds()
    {
        SetUpAccountHolderCreate(out var sent, out _);

        await Gateway().CreateAccountHolder(AccountHolderRequest(), CancellationToken.None);

        sent().Capabilities!.Keys.Should().BeEquivalentTo(
        [
            "receivePayments",
            "receiveFromPlatformPayments",
            "sendToTransferInstrument",
            "sendToBalanceAccount",
            "receiveFromBalanceAccount",
        ]);

        sent().Capabilities!.Values.Should().AllSatisfy(
            capability => capability.Requested.Should().BeTrue());
    }

    [Fact]
    public async Task CreateAccountHolder_NamesTheBalancePlatformAndTheLegalEntity()
    {
        SetUpAccountHolderCreate(out var sent, out _);

        var created = await Gateway().CreateAccountHolder(AccountHolderRequest(), CancellationToken.None);

        created.Should().Be(AccountHolderId);
        sent().BalancePlatform.Should().Be(BalancePlatformId);
        sent().LegalEntityId.Should().Be(LegalEntityId);
        sent().Reference.Should().Be("Pt000005_s0031610_ah");
    }

    /// <summary>
    /// The balance platform honours the idempotency header — verified live, where the same key
    /// returned the same account holder twice. This is the header that makes a retry safe.
    /// </summary>
    [Fact]
    public async Task CreateAccountHolder_SendsTheDerivedIdempotencyKey()
    {
        SetUpAccountHolderCreate(out _, out var options);

        await Gateway().CreateAccountHolder(
            AccountHolderRequest() with { OnboardingGeneration = 2 },
            CancellationToken.None);

        options()!.Headers[AdyenIdempotencyKey.HeaderName]
            .Should()
            .Be("wayroo-prod-31610-accountholder-g2");
    }

    /// <summary>
    /// The currency is stated on every balance-account create, because Adyen's default is EUR even on
    /// a US platform and the currency cannot be changed once the account exists. Getting this wrong
    /// is not a bug to patch — it is a seller whose earnings accumulate in the wrong currency until
    /// they are onboarded again.
    /// </summary>
    [Fact]
    public async Task CreateBalanceAccount_AlwaysStatesTheCurrency()
    {
        SetUpBalanceAccountCreate(out var sent, out _);

        await Gateway().CreateBalanceAccount(BalanceAccountRequest(), CancellationToken.None);

        sent().DefaultCurrencyCode.Should().Be("USD");
    }

    /// <summary>
    /// A balance account belongs to one seller's relationship with one tenant, which is what
    /// keeps earnings for different tenants from commingling — so unlike the legal entity, its
    /// reference is scoped to the store.
    /// </summary>
    [Fact]
    public async Task CreateBalanceAccount_CarriesTheStoreScopedReference()
    {
        SetUpBalanceAccountCreate(out var sent, out _);

        var created = await Gateway().CreateBalanceAccount(BalanceAccountRequest(), CancellationToken.None);

        created.Should().Be("BA329BG22322CJ5PZCLWPCK9R");
        sent().AccountHolderId.Should().Be(AccountHolderId);
        sent().Reference.Should().Be("Pt000005_s0031610_ba");
    }

    [Fact]
    public async Task CreateBalanceAccount_SendsTheDerivedIdempotencyKey()
    {
        SetUpBalanceAccountCreate(out _, out var options);

        await Gateway().CreateBalanceAccount(BalanceAccountRequest(), CancellationToken.None);

        options()!.Headers[AdyenIdempotencyKey.HeaderName]
            .Should()
            .Be("wayroo-prod-31610-balanceaccount-g1");
    }

    /// <summary>
    /// The balance platform reports errors in a different shape from Legal Entity Management, and
    /// reading the wrong one would lose Adyen's explanation entirely.
    /// </summary>
    [Fact]
    public async Task CreateBalanceAccount_SurfacesTheBalancePlatformsOwnExplanation()
    {
        var response = Refusal<ICreateBalanceAccountApiResponse, BclModels.BalanceAccount, BclModels.RestServiceError>(
            new BclModels.RestServiceError
            {
                ErrorCode = "30_014",
                Title = "Invalid account holder",
                Detail = "The account holder does not exist.",
                RequestId = "req-991",
            },
            System.Net.HttpStatusCode.UnprocessableContent);

        _balanceAccounts
            .Setup(s => s.CreateBalanceAccountAsync(
                It.IsAny<BclModels.BalanceAccountInfo>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var create = async () =>
            await Gateway().CreateBalanceAccount(BalanceAccountRequest(), CancellationToken.None);

        var refusal = (await create.Should().ThrowAsync<PaymentProviderException>()).Which;

        refusal.Message.Should().Be("The account holder does not exist.");
        refusal.ProviderStatusCode.Should().Be("30_014");
        refusal.Details.Should().Be("req-991");
    }

    [Fact]
    public async Task CreateOnboardingLink_ReturnsWhereToSendTheSeller()
    {
        var response = Ok<IGetLinkToAdyenhostedOnboardingPageApiResponse, LemModels.OnboardingLink>(
            new LemModels.OnboardingLink { Url = "https://onboarding.adyen.com/session/abc123" });

        _hostedOnboarding
            .Setup(s => s.GetLinkToAdyenhostedOnboardingPageAsync(
                LegalEntityId,
                It.IsAny<LemModels.OnboardingLinkInfo>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        var link = await Gateway().CreateOnboardingLink(
            new AdyenOnboardingLinkRequest { StoreId = StoreId, LegalEntityId = LegalEntityId },
            CancellationToken.None);

        link.Should().Be(new Uri("https://onboarding.adyen.com/session/abc123"));
    }

    /// <summary>
    /// The link authenticates the seller into their own onboarding session, so anyone who can read it
    /// can act as them — including anyone with access to the logs.
    /// </summary>
    [Fact]
    public async Task CreateOnboardingLink_KeepsTheLinkOutOfTheLogs()
    {
        var response = Ok<IGetLinkToAdyenhostedOnboardingPageApiResponse, LemModels.OnboardingLink>(
            new LemModels.OnboardingLink { Url = "https://onboarding.adyen.com/session/abc123" });

        _hostedOnboarding
            .Setup(s => s.GetLinkToAdyenhostedOnboardingPageAsync(
                LegalEntityId,
                It.IsAny<LemModels.OnboardingLinkInfo>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);

        await Gateway().CreateOnboardingLink(
            new AdyenOnboardingLinkRequest { StoreId = StoreId, LegalEntityId = LegalEntityId },
            CancellationToken.None);

        _logger.Entries.Should().NotContain(entry => entry.Message.Contains("abc123"));
        _logger.Entries
            .SelectMany(entry => entry.Properties.Values)
            .Should()
            .NotContain(value => (value as string ?? string.Empty).Contains("abc123"));
    }

    private static AdyenLegalEntityRequest LegalEntityRequest() => new()
    {
        TenantId = TenantId,
        StoreId = StoreId,
        FirstName = "Ada",
        LastName = "Lovelace",
        ResidentialCountry = "US",
    };

    private static AdyenAccountHolderRequest AccountHolderRequest() => new()
    {
        TenantId = TenantId,
        StoreId = StoreId,
        LegalEntityId = LegalEntityId,
        OnboardingGeneration = 1,
    };

    private static AdyenBalanceAccountRequest BalanceAccountRequest() => new()
    {
        TenantId = TenantId,
        StoreId = StoreId,
        AccountHolderId = AccountHolderId,
        CurrencyCode = "USD",
        OnboardingGeneration = 1,
    };

    private void SetUpLegalEntityCreate(out Func<LemModels.LegalEntityInfoRequiredType> sent)
    {
        LemModels.LegalEntityInfoRequiredType? captured = null;
        var response = Ok<ICreateLegalEntityApiResponse, LemModels.LegalEntity>(
            new LemModels.LegalEntity { Id = LegalEntityId });

        _legalEntities
            .Setup(s => s.CreateLegalEntityAsync(
                It.IsAny<LemModels.LegalEntityInfoRequiredType>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<LemModels.LegalEntityInfoRequiredType, RequestOptions, CancellationToken>(
                (payload, _, _) => captured = payload)
            .ReturnsAsync(response.Object);

        sent = () => captured ?? throw new InvalidOperationException("No legal entity create was sent.");
    }

    private void SetUpAccountHolderCreate(
        out Func<BclModels.AccountHolderInfo> sent,
        out Func<RequestOptions?> requestOptions)
    {
        BclModels.AccountHolderInfo? captured = null;
        RequestOptions? capturedOptions = null;
        var response = Ok<ICreateAccountHolderApiResponse, BclModels.AccountHolder>(
            new BclModels.AccountHolder { Id = AccountHolderId });

        _accountHolders
            .Setup(s => s.CreateAccountHolderAsync(
                It.IsAny<BclModels.AccountHolderInfo>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<BclModels.AccountHolderInfo, RequestOptions, CancellationToken>(
                (payload, options, _) =>
                {
                    captured = payload;
                    capturedOptions = options;
                })
            .ReturnsAsync(response.Object);

        sent = () => captured ?? throw new InvalidOperationException("No account holder create was sent.");
        requestOptions = () => capturedOptions;
    }

    private void SetUpBalanceAccountCreate(
        out Func<BclModels.BalanceAccountInfo> sent,
        out Func<RequestOptions?> requestOptions)
    {
        BclModels.BalanceAccountInfo? captured = null;
        RequestOptions? capturedOptions = null;
        var response = Ok<ICreateBalanceAccountApiResponse, BclModels.BalanceAccount>(
            new BclModels.BalanceAccount { Id = "BA329BG22322CJ5PZCLWPCK9R" });

        _balanceAccounts
            .Setup(s => s.CreateBalanceAccountAsync(
                It.IsAny<BclModels.BalanceAccountInfo>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<BclModels.BalanceAccountInfo, RequestOptions, CancellationToken>(
                (payload, options, _) =>
                {
                    captured = payload;
                    capturedOptions = options;
                })
            .ReturnsAsync(response.Object);

        sent = () => captured ?? throw new InvalidOperationException("No balance account create was sent.");
        requestOptions = () => capturedOptions;
    }
}
