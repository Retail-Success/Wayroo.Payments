using Amazon.Lambda.SQSEvents;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wayroo.Common.Exceptions;
using Wayroo.Common.Models.Events;
using Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways;
using Wayroo.Payments.Messages;
using Wayroo.Payments.Models;
using MigrationStates = Wayroo.Payments.Models.MigrationStates;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.UnitTests;

/// <summary>
/// The handler is pure orchestration: parser → store-owner resolver → repository upsert → routing
/// announcement. All provider-specific behaviour (body parsing, Orders SDK lookup) lives behind the
/// two seams and is covered by the provider-specific tests under <c>Gateways/{Provider}/</c>.
/// </summary>
public class PaymentConfigurationMessageHandlerTests
{
    private const string ProviderId = "propay";
    private const long AccountNumber = 718040110898;
    private const long StoreId = 1007;
    private const long TenantId = 42;
    private const string Body = "{\"any\":\"body content the parser knows how to read\"}";
    private const string Configuration = "{\"any\":\"configuration the parser chose to persist\"}";

    private readonly Mock<IPaymentConfigurationParser> _parser = new();
    private readonly Mock<IStoreOwnerResolver> _storeOwnerResolver = new();
    private readonly Mock<IPaymentConfigurationRepository> _repository = new();
    private readonly Mock<IIntegrationEventPublisher> _publisher = new();

    public PaymentConfigurationMessageHandlerTests()
    {
        _parser
            .Setup(p => p.Parse(Body))
            .Returns(new ParsedPaymentConfiguration(AccountNumber, ProviderId, Configuration));
        _storeOwnerResolver
            .Setup(r => r.Resolve(AccountNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreOwner(StoreId, TenantId));
        _repository
            .Setup(r => r.UpsertConfiguration(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentProviderConfiguration c, CancellationToken _) => c);

        // By default the store is new: no routing recorded, so the write creates one.
        GivenRouting(existing: null, writtenVersion: 1);
    }

    /// <summary>
    /// Sets up the routing read and write. <paramref name="existing"/> is what is already recorded;
    /// the write echoes back whatever the handler asked for, which is how the production repository
    /// behaves.
    /// </summary>
    private void GivenRouting(StoreRoutingConfiguration? existing, long writtenVersion)
    {
        _repository
            .Setup(r => r.GetRouting(StoreId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repository
            .Setup(r => r.UpsertRouting(It.IsAny<StoreRoutingConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoreRoutingConfiguration written, CancellationToken _) =>
                new StoreRoutingWriteResult(
                    new StoreRoutingConfiguration
                    {
                        StoreId = written.StoreId,
                        TenantId = written.TenantId,
                        AcquiringProviderId = written.AcquiringProviderId,
                        MigrationState = written.MigrationState,
                        ConfigurationVersion = writtenVersion,
                    },
                    existing));
    }

    private static StoreRoutingConfiguration Routing(string acquiringProviderId, long version) => new()
    {
        StoreId = StoreId,
        TenantId = TenantId,
        AcquiringProviderId = acquiringProviderId,
        MigrationState = MigrationStates.PropayActive,
        ConfigurationVersion = version,
    };

    private IntegrationEnvelope<StoreProviderConfigChanged>? PublishedEnvelope()
    {
        IntegrationEnvelope<StoreProviderConfigChanged>? captured = null;
        _publisher.Verify(
            p => p.PublishAsync(
                It.Is<IntegrationEnvelope<StoreProviderConfigChanged>>(e => Capture(e, ref captured)),
                It.IsAny<CancellationToken>()),
            Times.AtMostOnce);
        return captured;
    }

    private static bool Capture(
        IntegrationEnvelope<StoreProviderConfigChanged> envelope,
        ref IntegrationEnvelope<StoreProviderConfigChanged>? captured)
    {
        captured = envelope;
        return true;
    }

    private PaymentConfigurationMessageHandler CreateHandler() => new(
        _parser.Object,
        _storeOwnerResolver.Object,
        _repository.Object,
        _publisher.Object,
        NullLogger<PaymentConfigurationMessageHandler>.Instance);

    private static SQSEvent.SQSMessage MessageWith(string body)
        => new() { MessageId = Guid.NewGuid().ToString(), Body = body };

    [Fact]
    public async Task Handle_ParsesBody_ResolvesOwner_AndUpsertsConfigurationFromParsedValues()
    {
        _parser
            .Setup(p => p.Parse(Body))
            .Returns(new ParsedPaymentConfiguration(AccountNumber, ProviderId, Configuration));
        _storeOwnerResolver
            .Setup(r => r.Resolve(AccountNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreOwner(StoreId, TenantId));

        PaymentProviderConfiguration? captured = null;
        _repository
            .Setup(r => r.UpsertConfiguration(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentProviderConfiguration c, CancellationToken _) =>
            {
                captured = c;
                return c;
            });

        await CreateHandler().Handle(MessageWith(Body), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ProviderId.Should().Be(ProviderId);
        captured.StoreId.Should().Be(StoreId);
        captured.TenantId.Should().Be(TenantId);
        captured.AccountId.Should().Be(AccountNumber.ToString());
        captured.ProviderConfiguration.Should().Be(Configuration);
    }

    [Fact]
    public async Task Handle_PropagatesFormatException_FromParser_AndDoesNotResolveOrUpsert()
    {
        _parser
            .Setup(p => p.Parse(It.IsAny<string>()))
            .Throws(new FormatException("malformed body"));

        var act = async () => await CreateHandler().Handle(MessageWith("garbage"), CancellationToken.None);

        await act.Should().ThrowAsync<FormatException>();
        _storeOwnerResolver.Verify(
            r => r.Resolve(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(
            r => r.UpsertConfiguration(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_PropagatesResourceAccessException_FromResolver_AndDoesNotUpsert()
    {
        _parser
            .Setup(p => p.Parse(It.IsAny<string>()))
            .Returns(new ParsedPaymentConfiguration(AccountNumber, ProviderId, Configuration));
        _storeOwnerResolver
            .Setup(r => r.Resolve(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceAccessException("not yet onboarded"));

        var act = async () => await CreateHandler().Handle(MessageWith(Body), CancellationToken.None);

        await act.Should().ThrowAsync<ResourceAccessException>();
        _repository.Verify(
            r => r.UpsertConfiguration(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Seeing a store for the first time seeds its routing at the platform default and announces it.
    /// That seeding is what keeps the deploy inert: every existing store resolves to the provider it
    /// was already on.
    /// </summary>
    [Fact]
    public async Task Handle_SeedsRoutingAtTheDefaultProvider_AndAnnouncesIt_ForAStoreNotSeenBefore()
    {
        await CreateHandler().Handle(MessageWith(Body), CancellationToken.None);

        _repository.Verify(
            r => r.UpsertRouting(
                It.Is<StoreRoutingConfiguration>(routing =>
                    routing.StoreId == StoreId
                    && routing.TenantId == TenantId
                    && routing.AcquiringProviderId == ProviderIds.Propay
                    && routing.MigrationState == MigrationStates.PropayActive),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var envelope = PublishedEnvelope();
        envelope.Should().NotBeNull();
        envelope!.Data.AcquiringProviderId.Should().Be(ProviderIds.Propay);
        envelope.Data.MigrationState.Should().Be(MigrationStates.PropayActive);
        envelope.Data.PreviousAcquiringProviderId.Should().BeNull("there was nothing before");
    }

    /// <summary>
    /// The version is what a consumer uses to discard an update older than the one it has applied, so
    /// it has to travel as the envelope's sequence rather than being invented at publish time.
    /// </summary>
    [Fact]
    public async Task Handle_PublishesTheRoutingVersionAsTheEnvelopeSequence()
    {
        GivenRouting(existing: null, writtenVersion: 7);

        await CreateHandler().Handle(MessageWith(Body), CancellationToken.None);

        PublishedEnvelope()!.Metadata.Sequence.Should().Be(7);
    }

    /// <summary>
    /// A credential webhook is not an instruction to move a store between providers. Re-recording
    /// credentials for a store already routed must leave the routing where it is and say nothing.
    /// </summary>
    [Fact]
    public async Task Handle_SaysNothing_WhenTheRoutingIsUnchanged()
    {
        GivenRouting(existing: Routing(ProviderIds.Propay, version: 3), writtenVersion: 4);

        await CreateHandler().Handle(MessageWith(Body), CancellationToken.None);

        _repository.Verify(
            r => r.UpsertRouting(
                It.Is<StoreRoutingConfiguration>(routing => routing.AcquiringProviderId == ProviderIds.Propay),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _publisher.Verify(
            p => p.PublishAsync(
                It.IsAny<IntegrationEnvelope<StoreProviderConfigChanged>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_LeavesAnAlreadyMigratedStoreOnItsProvider()
    {
        // A store already moved to another provider must not be dragged back by a ProPay credential
        // webhook arriving during its tail.
        GivenRouting(existing: Routing(ProviderIds.Adyen, version: 9), writtenVersion: 10);

        await CreateHandler().Handle(MessageWith(Body), CancellationToken.None);

        _repository.Verify(
            r => r.UpsertRouting(
                It.Is<StoreRoutingConfiguration>(routing => routing.AcquiringProviderId == ProviderIds.Adyen),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _publisher.Verify(
            p => p.PublishAsync(
                It.IsAny<IntegrationEnvelope<StoreProviderConfigChanged>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ScopesTheEnvelopeToTheStoreAndTenant()
    {
        await CreateHandler().Handle(MessageWith(Body), CancellationToken.None);

        var metadata = PublishedEnvelope()!.Metadata;
        metadata.Scope.StoreId.Should().Be(StoreId.ToString());
        metadata.Scope.TenantId.Should().Be(TenantId.ToString());
    }

    /// <summary>
    /// The configuration is written before anything is announced. A publish failure re-queues the
    /// message and the write repeats idempotently; announcing first and then failing to write would
    /// tell consumers about a state this service does not hold.
    /// </summary>
    [Fact]
    public async Task Handle_WritesTheConfigurationBeforeAnnouncing()
    {
        _publisher
            .Setup(p => p.PublishAsync(
                It.IsAny<IntegrationEnvelope<StoreProviderConfigChanged>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceAccessException("throttled"));

        var act = async () => await CreateHandler().Handle(MessageWith(Body), CancellationToken.None);

        // Surfaced as transient so ProcessFailureHandler re-queues rather than dead-letters.
        await act.Should().ThrowAsync<ResourceAccessException>();
        _repository.Verify(
            r => r.UpsertConfiguration(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
