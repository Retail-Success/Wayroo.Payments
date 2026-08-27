using System.Text.Json;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wayroo.Common.Exceptions;
using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Eventing.UnitTests;

public class EventBridgeIntegrationEventPublisherTests
{
    private const string EventBusArn =
        "arn:aws:events:us-east-1:203538442868:event-bus/test-wayroo-events";

    private readonly Mock<IAmazonEventBridge> _eventBridge = new();

    private EventBridgeIntegrationEventPublisher CreatePublisher() => new(
        _eventBridge.Object,
        new EventBridgePublisherOptions { EventBusArn = EventBusArn },
        NullLogger<EventBridgeIntegrationEventPublisher>.Instance);

    private static IntegrationEnvelope<TestStoreEvent> Envelope() => IntegrationEnvelope.ForStore(
        new TestStoreEvent { ProviderId = "propay" },
        tenantId: "4",
        storeId: "103",
        correlationId: IntegrationEnvelope.NewCorrelationId(),
        sequence: 7);

    private void SetupFailure(string errorCode, string errorMessage) => _eventBridge
        .Setup(client => client.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PutEventsResponse
        {
            FailedEntryCount = 1,
            Entries = [new PutEventsResultEntry { ErrorCode = errorCode, ErrorMessage = errorMessage }],
        });

    [Fact]
    public async Task PublishAsync_SendsOneEntryDerivedFromTheEventType()
    {
        PutEventsRequest? captured = null;
        _eventBridge
            .Setup(client => client.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutEventsRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutEventsResponse { FailedEntryCount = 0 });

        var envelope = Envelope();

        await CreatePublisher().PublishAsync(envelope);

        captured.Should().NotBeNull();
        var entry = captured!.Entries.Should().ContainSingle().Subject;
        // PutEvents accepts a name or an ARN; we pass the ARN through unchanged.
        entry.EventBusName.Should().Be(EventBusArn);
        // Derived from the event type, never hand-written — this is what keeps the routed
        // detail-type consistent with the metadata inside the payload.
        entry.Source.Should().Be("rs.payments");
        entry.DetailType.Should().Be("payments.TestStoreThingHappened.v1");
        entry.Time.Should().Be(envelope.Metadata.OccurredAt.UtcDateTime);
    }

    [Fact]
    public async Task PublishAsync_SerializesTheEnvelopeWithIntegrationJson()
    {
        PutEventsRequest? captured = null;
        _eventBridge
            .Setup(client => client.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutEventsRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutEventsResponse { FailedEntryCount = 0 });

        var envelope = Envelope();

        await CreatePublisher().PublishAsync(envelope);

        var detail = captured!.Entries[0].Detail;

        // The detail must round-trip through the same options a consumer uses, and must carry the
        // dedupe/staleness metadata consumers depend on.
        var roundTripped = JsonSerializer.Deserialize<IntegrationEnvelope<TestStoreEvent>>(
            detail,
            IntegrationJson.Options);

        roundTripped.Should().NotBeNull();
        roundTripped!.Metadata.EventId.Should().Be(envelope.Metadata.EventId);
        roundTripped.Metadata.Sequence.Should().Be(7);
        roundTripped.Metadata.Scope.TenantId.Should().Be("4");
        roundTripped.Metadata.Scope.StoreId.Should().Be("103");
        roundTripped.Data.ProviderId.Should().Be("propay");
    }

    [Fact]
    public async Task PublishAsync_RepublishingTheSameEnvelope_KeepsTheEventIdSoConsumersCanDedupe()
    {
        var requests = new List<PutEventsRequest>();
        _eventBridge
            .Setup(client => client.PutEventsAsync(It.IsAny<PutEventsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutEventsRequest, CancellationToken>((request, _) => requests.Add(request))
            .ReturnsAsync(new PutEventsResponse { FailedEntryCount = 0 });

        var envelope = Envelope();
        var publisher = CreatePublisher();

        await publisher.PublishAsync(envelope);
        await publisher.PublishAsync(envelope);

        requests.Should().HaveCount(2);
        requests[0].Entries[0].Detail.Should().Be(requests[1].Entries[0].Detail);
    }

    [Fact]
    public async Task PublishAsync_TransientEntryFailure_ThrowsRetryable()
    {
        SetupFailure("ThrottlingException", "Rate exceeded");

        var act = () => CreatePublisher().PublishAsync(Envelope());

        // ResourceAccessException is what ProcessFailureHandler re-queues on, so the message gets
        // another attempt rather than going straight to the DLQ.
        (await act.Should().ThrowAsync<ResourceAccessException>())
            .WithMessage("*ThrottlingException*Rate exceeded*");
    }

    [Fact]
    public async Task PublishAsync_NonTransientEntryFailure_ThrowsHardFailure()
    {
        SetupFailure("NotAuthorizedForSourceException", "Not authorized for source rs.payments");

        var act = () => CreatePublisher().PublishAsync(Envelope());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task PublishAsync_NullEnvelope_Throws()
    {
        var act = () => CreatePublisher().PublishAsync<TestStoreEvent>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_BlankEventBusArn_Throws()
    {
        var act = () => new EventBridgeIntegrationEventPublisher(
            _eventBridge.Object,
            new EventBridgePublisherOptions { EventBusArn = "  " },
            NullLogger<EventBridgeIntegrationEventPublisher>.Instance);

        // An empty value is not an error to EventBridge — it silently means "default bus".
        act.Should().Throw<ArgumentException>();
    }
}
