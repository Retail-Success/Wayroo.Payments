using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Microsoft.Extensions.Logging;
using Wayroo.Common.Exceptions;
using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Eventing;

/// <summary>
/// The local <see cref="IIntegrationEventPublisher"/> implementation for Wayroo.Payments: serializes
/// the Wayroo.Common integration envelope with <see cref="IntegrationJson"/> and delivers it to the
/// intraprocess EventBridge bus (<c>{env}-wayroo-events</c>) with a single-entry <c>PutEvents</c>.
/// </summary>
/// <remarks>
/// Wayroo.Common carries no AWS dependency on purpose, so each service supplies this seam; this is
/// that implementation and it lives here rather than in RetailSuccess.Messaging (no shared-library
/// change — see the D4a story and architecture G of the Adyen migration plan).
/// <para>
/// Every entry field is derived from the event type — <c>Source</c> from <c>TData.Source</c>,
/// <c>DetailType</c> from <see cref="DetailType.Of{TEvent}"/> — so the routed <c>detail-type</c> can
/// never diverge from the metadata inside the payload.
/// </para>
/// <para>
/// One envelope per request. Batching is deliberately not implemented: <c>PutEvents</c> is not
/// atomic, and a single entry per call means a failure maps unambiguously onto the caller's retry
/// rather than needing per-entry bookkeeping. Callers must retry with the <b>same envelope
/// instance</b> — rebuilding one mints a fresh <c>EventId</c> and defeats consumer-side dedupe.
/// </para>
/// </remarks>
public sealed class EventBridgeIntegrationEventPublisher : IIntegrationEventPublisher
{
    /// <summary>
    /// Per-entry error codes that are worth retrying. Everything else (an authorization failure on
    /// the source or detail-type, for instance) will fail identically on a retry, so it is surfaced
    /// as a hard failure instead.
    /// </summary>
    private static readonly HashSet<string> RetryableErrorCodes =
        new(StringComparer.OrdinalIgnoreCase) { "ThrottlingException", "InternalFailure" };

    private readonly IAmazonEventBridge _eventBridge;
    private readonly ILogger<EventBridgeIntegrationEventPublisher> _logger;
    private readonly string _eventBusArn;

    public EventBridgeIntegrationEventPublisher(
        IAmazonEventBridge eventBridge,
        EventBridgePublisherOptions options,
        ILogger<EventBridgeIntegrationEventPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _eventBridge = eventBridge ?? throw new ArgumentNullException(nameof(eventBridge));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBusArn = string.IsNullOrWhiteSpace(options.EventBusArn)
            ? throw new ArgumentException(
                $"{nameof(EventBridgePublisherOptions)}.{nameof(EventBridgePublisherOptions.EventBusArn)} " +
                "must be supplied — an empty value resolves to the account's default bus.",
                nameof(options))
            : options.EventBusArn;
    }

    /// <inheritdoc />
    /// <exception cref="ResourceAccessException">
    /// The entry was rejected for a transient reason (throttling, internal failure). Retry with this
    /// same envelope instance.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The entry was rejected for a reason a retry won't fix (e.g. the caller isn't authorized to
    /// publish this source or detail-type to the bus).
    /// </exception>
    public async Task PublishAsync<TData>(
        IntegrationEnvelope<TData> envelope,
        CancellationToken cancellationToken = default)
        where TData : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var detailType = DetailType.Of<TData>();

        var request = new PutEventsRequest
        {
            Entries =
            [
                new PutEventsRequestEntry
                {
                    // PutEvents takes a name or an ARN here; we pass the ARN.
                    EventBusName = _eventBusArn,
                    Source = TData.Source,
                    DetailType = detailType,
                    Detail = IntegrationJson.SerializeDetail(envelope),
                    // Stamp the wire `time` from the envelope so the EventBridge event, the archive
                    // entry and the payload's OccurredAt all agree — the default is "now", which
                    // drifts from OccurredAt on a retry.
                    Time = envelope.Metadata.OccurredAt.UtcDateTime,
                },
            ],
        };

        var response = await _eventBridge.PutEventsAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.FailedEntryCount > 0)
        {
            throw BuildFailure(response, envelope.Metadata.EventId, detailType);
        }

        _logger.LogInformation(
            "Published {DetailType} {EventId} to {EventBus}",
            detailType,
            envelope.Metadata.EventId,
            _eventBusArn);
    }

    private Exception BuildFailure(PutEventsResponse response, Guid eventId, string detailType)
    {
        var failed = response.Entries?.FirstOrDefault(entry => !string.IsNullOrEmpty(entry.ErrorCode));
        var errorCode = failed?.ErrorCode ?? "Unknown";
        var message =
            $"PutEvents rejected {detailType} {eventId} on {_eventBusArn}: {errorCode} " +
            $"{failed?.ErrorMessage}".TrimEnd();

        _logger.LogError(
            "PutEvents rejected {DetailType} {EventId} on {EventBus}: {ErrorCode} {ErrorMessage}",
            detailType,
            eventId,
            _eventBusArn,
            errorCode,
            failed?.ErrorMessage);

        return RetryableErrorCodes.Contains(errorCode)
            ? new ResourceAccessException(message)
            : new InvalidOperationException(message);
    }
}
