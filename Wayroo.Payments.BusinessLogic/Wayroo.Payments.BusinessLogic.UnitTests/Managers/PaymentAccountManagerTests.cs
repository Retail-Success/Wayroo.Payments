using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Wayroo.Payments.BusinessLogic.Gateways;
using Wayroo.Payments.BusinessLogic.Managers;
using Wayroo.Payments.BusinessLogic.UnitTests.TestDoubles;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.UnitTests.Managers;

/// <summary>
/// The provider-resolution rule. Every branch matters: getting it wrong means reading a balance from,
/// or acting against, the wrong merchant account.
/// </summary>
public class PaymentAccountManagerTests
{
    private const long TenantId = 42;
    private const long StoreId = 1007;

    private readonly Mock<IPaymentConfigurationRepository> _repository = new();
    private readonly Dictionary<string, Mock<IPaymentAccountGateway>> _gateways = [];
    private readonly RecordingLogger<PaymentAccountManager> _logger = new();
    private string _defaultProviderId = "propay";

    private Mock<IPaymentAccountGateway> Register(string providerId)
    {
        var gateway = new Mock<IPaymentAccountGateway>();
        gateway.SetupGet(g => g.ProviderId).Returns(providerId);
        gateway
            .Setup(g => g.GetBalance(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentAccountBalance { AccountExists = true, ProviderId = providerId });
        gateway
            .Setup(g => g.RefreshAccountDetails(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentAccountDetails { AccountExists = true, ProviderId = providerId });

        _gateways[providerId] = gateway;
        return gateway;
    }

    private void GivenRouting(string? acquiringProviderId)
        => _repository
            .Setup(r => r.GetRouting(StoreId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(acquiringProviderId is null
                ? null
                : new StoreRoutingConfiguration
                {
                    StoreId = StoreId,
                    AcquiringProviderId = acquiringProviderId,
                    MigrationState = MigrationStates.PropayActive,
                });

    private PaymentAccountManager Manager() => new(
        _repository.Object,
        new PaymentGatewayRegistry(_gateways.Values.Select(g => g.Object)),
        Options.Create(new PaymentGatewayOptions { DefaultProviderId = _defaultProviderId }),
        _logger);

    private Task<PaymentAccountBalance> GetBalance(string? providerId = null)
        => Manager().GetBalance(TenantId, StoreId, providerId, CancellationToken.None);

    /// <summary>
    /// The common case, not an edge: a routing record only appears once the recorder has seen the
    /// store. Falling back is what keeps the mechanism inert — nothing needs backfilling for existing
    /// stores to keep resolving exactly as they did.
    /// </summary>
    [Fact]
    public async Task AStoreWithNoRoutingRecord_FallsBackToTheDefaultProvider()
    {
        var propay = Register("propay");
        Register("adyen");
        GivenRouting(null);

        var balance = await GetBalance();

        balance.ProviderId.Should().Be("propay");
        propay.Verify(g => g.GetBalance(TenantId, StoreId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ARoutedStore_ResolvesToItsAcquiringProvider()
    {
        Register("propay");
        var adyen = Register("adyen");
        GivenRouting("adyen");

        var balance = await GetBalance();

        balance.ProviderId.Should().Be("adyen");
        adyen.Verify(g => g.GetBalance(TenantId, StoreId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("ADYEN")]
    [InlineData("  adyen  ")]
    public async Task RoutingMatchesTheGatewayCaseInsensitively(string acquiringProviderId)
    {
        Register("propay");
        Register("adyen");
        GivenRouting(acquiringProviderId);

        (await GetBalance()).ProviderId.Should().Be("adyen");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankAcquiringProvider_FallsBackToTheDefault(string acquiringProviderId)
    {
        Register("propay");
        GivenRouting(acquiringProviderId);

        (await GetBalance()).ProviderId.Should().Be("propay");
    }

    /// <summary>
    /// A store deliberately pointed at a provider this service cannot reach must not be served quietly
    /// from the old one — that would be the wrong answer given confidently.
    /// </summary>
    [Fact]
    public async Task AStoreRoutedToAProviderWithNoGateway_Throws()
    {
        Register("propay");
        GivenRouting("adyen");

        var act = async () => await GetBalance();

        var failure = await act.Should().ThrowAsync<PaymentProviderAmbiguousException>();
        failure.Which.StoreId.Should().Be(StoreId);
        failure.Which.CandidateProviderIds.Should().BeEquivalentTo("adyen");
    }

    [Fact]
    public async Task AnExplicitProvider_OverridesWhatTheStoreIsRecordedAgainst()
    {
        var propay = Register("propay");
        var adyen = Register("adyen");
        GivenRouting("adyen");

        var balance = await GetBalance("propay");

        balance.ProviderId.Should().Be("propay");
        propay.Verify(g => g.GetBalance(TenantId, StoreId, It.IsAny<CancellationToken>()), Times.Once);
        adyen.Verify(
            g => g.GetBalance(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AnExplicitProvider_ShortCircuitsTheLookupEntirely()
    {
        Register("propay");

        await GetBalance("propay");

        _repository.Verify(
            r => r.GetRouting(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AnExplicitProviderWithNoGateway_Throws()
    {
        Register("propay");

        var act = async () => await GetBalance("stripe");

        // Not "ambiguous": the caller's input is the problem, and it answers as a 400 rather than a
        // conflict an operator has to settle.
        var failure = await act.Should().ThrowAsync<PaymentProviderNotSupportedException>();
        failure.Which.RequestedProviderId.Should().Be("stripe");
        failure.Which.KnownProviderIds.Should().BeEquivalentTo("propay");
    }

    [Fact]
    public async Task ADefaultProviderWithNoGateway_Throws()
    {
        _defaultProviderId = "adyen";
        Register("propay");
        GivenRouting(null);

        var act = async () => await GetBalance();

        await act.Should().ThrowAsync<PaymentProviderAmbiguousException>();
    }

    [Fact]
    public async Task GetBalance_ReportsNoAccount_WhenTheGatewayFindsNone()
    {
        Register("propay")
            .Setup(g => g.GetBalance(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentAccountBalance?)null);
        GivenRouting(null);

        var balance = await GetBalance();

        balance.AccountExists.Should().BeFalse();
        balance.ProviderId.Should().Be("propay");
    }

    [Fact]
    public async Task RefreshAccount_ReportsNoAccount_WhenTheGatewayFindsNone()
    {
        Register("propay")
            .Setup(g => g.RefreshAccountDetails(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentAccountDetails?)null);
        GivenRouting(null);

        var details = await Manager().RefreshAccount(TenantId, StoreId, null, null, CancellationToken.None);

        details.AccountExists.Should().BeFalse();
        details.ProviderId.Should().Be("propay");
    }

    [Fact]
    public async Task RefreshAccount_ResolvesTheProviderTheSameWayAsGetBalance()
    {
        Register("propay");
        var adyen = Register("adyen");
        GivenRouting("adyen");

        var details = await Manager().RefreshAccount(TenantId, StoreId, null, null, CancellationToken.None);

        details.ProviderId.Should().Be("adyen");
        adyen.Verify(
            g => g.RefreshAccountDetails(TenantId, StoreId, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// The seed a backfill supplies has to reach the gateway; it is the only way a store this service
    /// holds no reference for can be recorded.
    /// </summary>
    [Fact]
    public async Task RefreshAccount_ForwardsTheSuppliedAccountReferenceToTheGateway()
    {
        var propay = Register("propay");
        GivenRouting(null);

        await Manager().RefreshAccount(TenantId, StoreId, null, "718040110898", CancellationToken.None);

        propay.Verify(
            g => g.RefreshAccountDetails(TenantId, StoreId, "718040110898", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshAccount_EmitsTheNoAccountSignal_WhenTheGatewayFindsNothing()
    {
        // The CloudWatch alarm {env}-WayrooPayments-API-RefreshAccount-NoAccount counts this property.
        // Drop it, or rename the constant on one side only, and the metric filter stops matching — at
        // which point the alarm reports no data and reads as healthy rather than as broken.
        var gateway = Register("propay");
        gateway
            .Setup(g => g.RefreshAccountDetails(
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentAccountDetails?)null);
        GivenRouting(null);

        var details = await Manager().RefreshAccount(TenantId, StoreId, null, null, CancellationToken.None);

        details.AccountExists.Should().BeFalse();

        var entry = _logger.EntryForSignal(PaymentsLogSignals.RefreshAccountNoAccount);
        // Information, not Warning: one of these is ordinary and only the rate is alarming, which is
        // why the alarm has a threshold rather than firing on any occurrence.
        entry.Level.Should().Be(LogLevel.Information);
        entry.Properties["StoreId"].Should().Be(StoreId);
        entry.Properties["TenantId"].Should().Be(TenantId);
        entry.Properties["ProviderId"].Should().Be("propay");
    }

    [Fact]
    public async Task RefreshAccount_DoesNotEmitTheNoAccountSignal_WhenAnAccountIsFound()
    {
        Register("propay");
        GivenRouting(null);

        await Manager().RefreshAccount(TenantId, StoreId, null, null, CancellationToken.None);

        _logger.HasSignal(PaymentsLogSignals.RefreshAccountNoAccount).Should().BeFalse();
    }
}
