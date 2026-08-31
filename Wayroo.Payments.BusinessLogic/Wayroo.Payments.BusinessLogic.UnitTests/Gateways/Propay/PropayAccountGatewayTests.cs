using System.Diagnostics;
using System.Net.Http;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RetailSuccess.PaymentGateway.Propay;
using RetailSuccess.PaymentGateway.Propay.Abstractions;
using RetailSuccess.PaymentGateway.Propay.Models;
using RetailSuccess.PaymentGateway.Propay.Requests;
using Wayroo.Payments.BusinessLogic.Gateways;
using Wayroo.Payments.BusinessLogic.Gateways.Propay;
using Wayroo.Payments.BusinessLogic.UnitTests.TestDoubles;
using Wayroo.Payments.Models;
using PropayAccountDetail = RetailSuccess.PaymentGateway.Propay.Responses.GetAccountDetailsResponse;
using PropayBalance = RetailSuccess.PaymentGateway.Propay.Responses.GetAccountBalanceResponse;

namespace Wayroo.Payments.BusinessLogic.UnitTests.Gateways.Propay;

/// <summary>
/// Unit tests for <see cref="PropayAccountGateway"/> — the seam where ProPay's cent-denominated,
/// two-call, twenty-four-status world is turned into the neutral contract.
/// </summary>
public class PropayAccountGatewayTests
{
    private const long TenantId = 42;
    private const long StoreId = 1007;
    private const long AccountNumber = 718040110898;

    private readonly Mock<IPropayClient> _propayClient = new(MockBehavior.Strict);
    private readonly Mock<IPaymentConfigurationRepository> _repository = new();
    private readonly RecordingLogger<PropayAccountGateway> _logger = new();
    private readonly PropayAccountGateway _gateway;

    public PropayAccountGatewayTests()
    {
        _gateway = new PropayAccountGateway(
            _propayClient.Object,
            _repository.Object,
            _logger);

        _repository
            .Setup(r => r.UpsertAccountDetails(
                It.IsAny<PaymentProviderConfiguration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentProviderConfiguration configuration, CancellationToken _) => configuration);
    }

    private static PropayStatus Ok => new("00", "Success");

    /// <summary>A ProPay balance response. Amounts are in cents, as ProPay reports them.</summary>
    private static PropayBalance Balance(
        int availableCents = 150_050,
        int pendingCents = 25_075,
        int reserveCents = 1_000,
        string achOutEnabled = "Y")
        => new(
            AccountNumber,
            availableCents,
            pendingCents,
            reserveCents,
            "merchant@example.com",
            new TransferMethodInfo("1234", achOutEnabled, "$", 0, 0m),
            new TransferMethodInfo("1234", "N", "$", 0, 0m));

    private static PropayAccountDetail Detail(string accountStatus = "ReadyToProcess", string apiReady = "Y")
        => new(
            status: "00",
            accountStatus: accountStatus,
            accountNumber: AccountNumber.ToString(),
            affiliation: "WAYROO",
            apiReady: apiReady,
            address: "1 Example Way",
            city: "Lehi",
            state: "UT",
            zip: "84043",
            currencyCode: "USD",
            expiration: new DateTime(2030, 1, 1),
            signupDate: new DateTime(2020, 6, 1),
            tier: "PPZ_NCR",
            creditCardTransactionLimit: 1_000m,
            creditCardMonthLimit: 10_000m,
            achPaymentPerTranLimit: 500m,
            achPaymentMonthLimit: 5_000m,
            creditCardMonthlyVolume: 250m,
            achPaymentMonthlyVolume: 100m,
            reserveBalance: 10m,
            sourceEmail: "merchant@example.com",
            visaCheckoutMerchantId: null,
            masterPassCheckoutMerchantId: null);

    private void GivenRecordedConfiguration(PaymentProviderConfiguration? configuration)
        => _repository
            .Setup(r => r.GetConfiguration(StoreId, "propay", It.IsAny<CancellationToken>()))
            .ReturnsAsync(configuration);

    private static PaymentProviderConfiguration Recorded(PaymentAccountStatus? status) => new()
    {
        StoreId = StoreId,
        ProviderId = "propay",
        AccountId = AccountNumber.ToString(),
        TenantId = TenantId,
        AccountStatus = status,
    };

    private void GivenBalance(PropayBalance balance)
        => _propayClient
            .Setup(c => c.GetAccountBalance(TenantId, AccountNumber))
            .ReturnsAsync(new PropayResult<PropayBalance>.Success(balance, Ok));

    private void GivenAccountDetail(PropayAccountDetail detail)
        => _propayClient
            .Setup(c => c.GetAccountDetails(TenantId, It.IsAny<GetAccountDetailsRequest>()))
            .ReturnsAsync(new PropayResult<PropayAccountDetail>.Success(detail, Ok));

    [Fact]
    public async Task GetBalance_ConvertsPropaysCentsIntoMajorUnits()
    {
        GivenRecordedConfiguration(Recorded(PaymentAccountStatus.ReadyToProcess));
        GivenBalance(Balance(availableCents: 150_050, pendingCents: 25_075, reserveCents: 1_000));

        var balance = await _gateway.GetBalance(TenantId, StoreId, CancellationToken.None);

        // The single most expensive mistake available here is skipping this division.
        balance!.AvailableBalance!.Amount.Should().Be(1500.50m);
        balance.PendingBalance!.Amount.Should().Be(250.75m);
        balance.ReserveBalance!.Amount.Should().Be(10.00m);
        balance.AvailableBalance.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task GetBalance_ReportsPayoutCapabilityFromAchOut()
    {
        GivenRecordedConfiguration(Recorded(PaymentAccountStatus.ReadyToProcess));
        GivenBalance(Balance(achOutEnabled: "Y"));

        var balance = await _gateway.GetBalance(TenantId, StoreId, CancellationToken.None);

        balance!.CanReceivePayouts.Should().BeTrue();
    }

    [Fact]
    public async Task GetBalance_ReportsPayoutsDisabled_WhenAchOutIsOff()
    {
        GivenRecordedConfiguration(Recorded(PaymentAccountStatus.ReadyToProcess));
        GivenBalance(Balance(achOutEnabled: "N"));

        var balance = await _gateway.GetBalance(TenantId, StoreId, CancellationToken.None);

        // An account can keep selling while payouts are held; the two flags are independent.
        balance!.CanReceivePayouts.Should().BeFalse();
        balance.CanProcessPayments.Should().BeTrue();
    }

    [Fact]
    public async Task GetBalance_UsesTheRecordedStatus_WithoutCallingTheProviderForIt()
    {
        GivenRecordedConfiguration(Recorded(PaymentAccountStatus.Suspended));
        GivenBalance(Balance());

        var balance = await _gateway.GetBalance(TenantId, StoreId, CancellationToken.None);

        balance!.Status.Should().Be(PaymentAccountStatus.Suspended);
        balance.CanProcessPayments.Should().BeFalse();
        _propayClient.Verify(
            c => c.GetAccountDetails(It.IsAny<long>(), It.IsAny<GetAccountDetailsRequest>()),
            Times.Never);
    }

    [Fact]
    public async Task GetBalance_ReadsAndRecordsTheStatus_WhenTheStoreHasNotBeenBackfilled()
    {
        // The account is known but its standing has never been recorded — the state most stores are
        // in before the backfill runs.
        GivenRecordedConfiguration(Recorded(status: null));
        GivenBalance(Balance());
        GivenAccountDetail(Detail("ReadyToProcess"));

        var balance = await _gateway.GetBalance(TenantId, StoreId, CancellationToken.None);

        // Never null when the account exists: Luci.Integrations.Api casts this unconditionally.
        balance!.Status.Should().Be(PaymentAccountStatus.ReadyToProcess);
        balance.CanProcessPayments.Should().BeTrue();
        balance.StatusIsProvisional.Should().BeFalse();

        // And the store heals, so the next read is a single provider call again.
        _repository.Verify(
            r => r.UpsertAccountDetails(
                It.Is<PaymentProviderConfiguration>(c => c.AccountStatus == PaymentAccountStatus.ReadyToProcess),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetBalance_StillServesTheBalance_WhenTheProviderRefusesTheAccountDetail()
    {
        // The balance call succeeded; only the second, secondary read failed. Losing the whole
        // response over it would take the merchant's balance off the screen for a value the caller
        // may not even use.
        GivenRecordedConfiguration(Recorded(status: null));
        GivenBalance(Balance(availableCents: 150_050));
        _propayClient
            .Setup(c => c.GetAccountDetails(TenantId, It.IsAny<GetAccountDetailsRequest>()))
            .ReturnsAsync(new PropayResult<PropayAccountDetail>.Failure(
                new PropayStatus("24", "Invalid Source Email")));

        var balance = await _gateway.GetBalance(TenantId, StoreId, CancellationToken.None);

        balance!.AccountExists.Should().BeTrue();
        balance.AvailableBalance!.Amount.Should().Be(1500.50m);

        // Still non-null, but flagged as a placeholder and failing closed on the capability.
        balance.Status.Should().Be(PaymentAccountStatus.Pending);
        balance.StatusIsProvisional.Should().BeTrue();
        balance.CanProcessPayments.Should().BeFalse();
    }

    [Fact]
    public async Task GetBalance_StillServesTheBalance_WhenTheAccountDetailReadThrows()
    {
        GivenRecordedConfiguration(Recorded(status: null));
        GivenBalance(Balance());
        _propayClient
            .Setup(c => c.GetAccountDetails(TenantId, It.IsAny<GetAccountDetailsRequest>()))
            .ThrowsAsync(new HttpRequestException("ProPay's XML endpoint is unreachable."));

        var balance = await _gateway.GetBalance(TenantId, StoreId, CancellationToken.None);

        balance!.AccountExists.Should().BeTrue();
        balance.Status.Should().Be(PaymentAccountStatus.Pending);
        balance.StatusIsProvisional.Should().BeTrue();
    }

    [Fact]
    public async Task GetBalance_ServesTheRealStatus_WhenOnlyRecordingItFails()
    {
        // The standing was read successfully, so serve it — the store just does not heal this time.
        GivenRecordedConfiguration(Recorded(status: null));
        GivenBalance(Balance());
        GivenAccountDetail(Detail("ReadyToProcess"));
        _repository
            .Setup(r => r.UpsertAccountDetails(
                It.IsAny<PaymentProviderConfiguration>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DynamoDB is having a moment."));

        var balance = await _gateway.GetBalance(TenantId, StoreId, CancellationToken.None);

        balance!.Status.Should().Be(PaymentAccountStatus.ReadyToProcess);
        balance.CanProcessPayments.Should().BeTrue();
        balance.StatusIsProvisional.Should().BeFalse();
    }

    [Fact]
    public async Task GetBalance_DoesNotAbsorbTheFailure_WhenTheCallerCancelled()
    {
        // Nothing to be robust for: the response is going nowhere, and swallowing this would hide a
        // cancelled request as a store with an unknown standing.
        using var cancellation = new CancellationTokenSource();
        GivenRecordedConfiguration(Recorded(status: null));
        GivenBalance(Balance());
        _propayClient
            .Setup(c => c.GetAccountDetails(TenantId, It.IsAny<GetAccountDetailsRequest>()))
            .Returns(async () =>
            {
                await cancellation.CancelAsync();
                cancellation.Token.ThrowIfCancellationRequested();
                throw new UnreachableException();
            });

        var act = async () => await _gateway.GetBalance(TenantId, StoreId, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RefreshAccountDetails_StillFails_WhenRecordingFails()
    {
        // The balance path tolerates a failed write; this one must not. Recording is the whole point
        // of the call, and a backfill that is told "recorded" without a write would skip the store.
        GivenRecordedConfiguration(Recorded(status: null));
        GivenAccountDetail(Detail("ReadyToProcess"));
        _repository
            .Setup(r => r.UpsertAccountDetails(
                It.IsAny<PaymentProviderConfiguration>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DynamoDB is having a moment."));

        var act = async () => await _gateway.RefreshAccountDetails(
            TenantId,
            StoreId,
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetBalance_ReportsNoAccount_WhenNoReferenceIsRecorded()
    {
        // Nothing recorded and nothing to fall back on: this service does not call another Wayroo
        // service to go looking, so the honest answer is that it knows of no account.
        GivenRecordedConfiguration(null);

        var balance = await _gateway.GetBalance(TenantId, StoreId, CancellationToken.None);

        balance.Should().BeNull();
        _propayClient.Verify(c => c.GetAccountBalance(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task GetBalance_SurfacesAProviderRefusalAsAProviderException()
    {
        GivenRecordedConfiguration(Recorded(PaymentAccountStatus.ReadyToProcess));
        _propayClient
            .Setup(c => c.GetAccountBalance(TenantId, AccountNumber))
            .ReturnsAsync(new PropayResult<PropayBalance>.Failure(
                new PropayStatus("24", "Invalid Source Email", "Check the sourceEmail value.")));

        var act = async () => await _gateway.GetBalance(TenantId, StoreId, CancellationToken.None);

        var failure = await act.Should().ThrowAsync<PaymentProviderException>();
        failure.Which.Message.Should().Be("Invalid Source Email");
        failure.Which.ProviderStatusCode.Should().Be("24");
        failure.Which.Details.Should().Be("Check the sourceEmail value.");
    }

    [Fact]
    public async Task RefreshAccountDetails_RecordsTheAccountInformationAgainstTheStore()
    {
        GivenRecordedConfiguration(Recorded(status: null));
        GivenAccountDetail(Detail("Hold"));

        var details = await _gateway.RefreshAccountDetails(TenantId, StoreId, null, CancellationToken.None);

        details!.Status.Should().Be(PaymentAccountStatus.Suspended);
        details.ProviderStatusCode.Should().Be("Hold");
        details.CanProcessPayments.Should().BeFalse();
        details.Tier.Should().Be("PPZ_NCR");
        details.Affiliation.Should().Be("WAYROO");
        details.ApiReady.Should().BeTrue();
        details.CurrencyCode.Should().Be("USD");
        // The account detail already reports major units, unlike the balance endpoint's cents.
        details.CardTransactionLimit!.Amount.Should().Be(1_000m);
        details.ReserveBalance!.Amount.Should().Be(10m);

        _repository.Verify(
            r => r.UpsertAccountDetails(
                It.Is<PaymentProviderConfiguration>(c =>
                    c.StoreId == StoreId
                    && c.TenantId == TenantId
                    && c.ProviderId == "propay"
                    && c.AccountId == AccountNumber.ToString()
                    && c.AccountStatus == PaymentAccountStatus.Suspended
                    && c.ProviderAccountDetails != null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// The refresh must never write <see cref="PaymentProviderConfiguration.ProviderConfiguration"/>
    /// — that attribute belongs to the credential webhook, and the repository's update expression is
    /// built from the model it is handed.
    /// </summary>
    [Fact]
    public async Task RefreshAccountDetails_DoesNotTouchTheCredentialPayload()
    {
        GivenRecordedConfiguration(Recorded(status: null));
        GivenAccountDetail(Detail());

        await _gateway.RefreshAccountDetails(TenantId, StoreId, null, CancellationToken.None);

        _repository.Verify(
            r => r.UpsertAccountDetails(
                It.Is<PaymentProviderConfiguration>(c => c.ProviderConfiguration == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(
            r => r.UpsertConfiguration(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// The backfill's seeding path: the driver knows the account reference, so it supplies one for a
    /// store this service has never recorded, and the refresh records it.
    /// </summary>
    [Fact]
    public async Task RefreshAccountDetails_UsesASuppliedAccountReference_WhenNothingIsRecorded()
    {
        GivenRecordedConfiguration(null);
        GivenAccountDetail(Detail());

        var details = await _gateway.RefreshAccountDetails(
            TenantId,
            StoreId,
            AccountNumber.ToString(),
            CancellationToken.None);

        details!.ProviderAccountRef.Should().Be(AccountNumber.ToString());
        _repository.Verify(
            r => r.UpsertAccountDetails(
                It.Is<PaymentProviderConfiguration>(c => c.AccountId == AccountNumber.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshAccountDetails_PrefersASuppliedReferenceOverTheRecordedOne()
    {
        GivenRecordedConfiguration(Recorded(status: null));
        GivenAccountDetail(Detail());
        const long supplied = 999888777666;
        _propayClient
            .Setup(c => c.GetAccountDetails(TenantId, It.IsAny<GetAccountDetailsRequest>()))
            .ReturnsAsync(new PropayResult<PropayAccountDetail>.Success(Detail(), Ok));

        var details = await _gateway.RefreshAccountDetails(
            TenantId,
            StoreId,
            supplied.ToString(),
            CancellationToken.None);

        details!.ProviderAccountRef.Should().Be(supplied.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    public async Task RefreshAccountDetails_IgnoresAnUnusableSuppliedReference(string providerAccountRef)
    {
        GivenRecordedConfiguration(Recorded(status: null));
        GivenAccountDetail(Detail());

        var details = await _gateway.RefreshAccountDetails(
            TenantId,
            StoreId,
            providerAccountRef,
            CancellationToken.None);

        // Falls through to what is recorded rather than failing or passing rubbish to the provider.
        details!.ProviderAccountRef.Should().Be(AccountNumber.ToString());
    }

    [Fact]
    public async Task RefreshAccountDetails_FailsClosed_OnAStatusPropayHasNotUsedBefore()
    {
        GivenRecordedConfiguration(Recorded(status: null));
        GivenAccountDetail(Detail("SomethingProPayInventedLastTuesday"));

        var details = await _gateway.RefreshAccountDetails(TenantId, StoreId, null, CancellationToken.None);

        // Reading an unknown status as sellable is the expensive direction to be wrong in.
        details!.Status.Should().Be(PaymentAccountStatus.Suspended);
        details.CanProcessPayments.Should().BeFalse();
        details.ProviderStatusCode.Should().Be("SomethingProPayInventedLastTuesday");
    }

    [Fact]
    public async Task RefreshAccountDetails_ReportsNoAccount_WhenNothingIsRecordedAndNothingSupplied()
    {
        GivenRecordedConfiguration(null);

        var details = await _gateway.RefreshAccountDetails(TenantId, StoreId, null, CancellationToken.None);

        details.Should().BeNull();
        _repository.Verify(
            r => r.UpsertAccountDetails(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetBalance_EmitsTheStatusBackfilledSignal_WhenTheStoreHasNotBeenBackfilled()
    {
        // The CloudWatch alarm {env}-WayrooPayments-API-GetBalance-StatusBackfilled counts this
        // property. It is what tells us stores are still costing a second provider call per balance
        // read, and it should trend to zero as the backfill lands.
        GivenRecordedConfiguration(Recorded(status: null));
        GivenBalance(Balance());
        GivenAccountDetail(Detail("ReadyToProcess"));

        await _gateway.GetBalance(TenantId, StoreId, CancellationToken.None);

        var entry = _logger.EntryForSignal(PaymentsLogSignals.GetBalanceStatusBackfilled);
        entry.Level.Should().Be(LogLevel.Information);
        entry.Properties["StoreId"].Should().Be(StoreId);
        entry.Properties["TenantId"].Should().Be(TenantId);
        entry.Properties["ProviderId"].Should().Be("propay");
    }

    [Fact]
    public async Task GetBalance_DoesNotEmitTheStatusBackfilledSignal_WhenTheStandingIsAlreadyRecorded()
    {
        // The healed case, which is what the alarm's threshold assumes becomes the norm.
        GivenRecordedConfiguration(Recorded(PaymentAccountStatus.ReadyToProcess));
        GivenBalance(Balance());

        await _gateway.GetBalance(TenantId, StoreId, CancellationToken.None);

        _logger.HasSignal(PaymentsLogSignals.GetBalanceStatusBackfilled).Should().BeFalse();
    }
}
