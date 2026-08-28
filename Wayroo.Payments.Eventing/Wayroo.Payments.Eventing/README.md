# Wayroo.Payments.Eventing

Publishes Wayroo.Payments integration events to the **intraprocess EventBridge bus**
(`{env}-wayroo-events`).

Wayroo.Payments sits between two EventBridge buses, and it matters which is which:

| Bus | Direction | Who touches it |
| --- | --- | --- |
| `{env}-webhook-bus` | **inbound** — provider webhook ingress | the ConfigurationRecorder consumes it, via a rule into its SQS queue |
| `{env}-wayroo-events` | **outbound** — intraprocess (service-to-service) events | this library publishes to it |

Both buses are provisioned by the Infrastructure team's common project. Wayroo.Payments imports them
by ARN and creates neither.

This is the local `IIntegrationEventPublisher` implementation for this service. Wayroo.Common defines
the envelope but deliberately carries no AWS dependency, so each service supplies the delivery seam;
this is ours. No shared-library (`RetailSuccess.Messaging`) change is involved.

| Piece | Where |
| --- | --- |
| Envelope, `IIntegrationEvent`, `DetailType`, `IntegrationJson` | `Wayroo.Common` (>= 2.1.1) |
| `PutEvents` delivery | `EventBridgeIntegrationEventPublisher` (this project) |
| The bus ARN reaching the host | `WayrooEventsBusArn` stack parameter -> recorder lambda env var (`Wayroo.Payments.Infrastructure`) |
| The bus itself, its archive, its logging, the `events:PutEvents` grant | Infrastructure team's common project (see that project; `ByDesign.Infrastructure/modules/eventbus` is the reference shape) |

## Publishing

```csharp
services.AddPaymentsEventPublishing(configuration);   // needs WayrooEventsBusArn
```

```csharp
var envelope = IntegrationEnvelope.ForStore(
    new StoreProviderConfigChanged { /* ... */ },
    tenantId: tenantId,
    storeId: storeId,
    correlationId: correlationId,   // propagate the inbound one where there is one
    sequence: configurationVersion);

await publisher.PublishAsync(envelope, cancellationToken);
```

Rules that matter:

- **Retry with the same envelope instance.** Delivery is at-least-once and consumers dedupe on
  `Metadata.EventId`; rebuilding the envelope for a retry mints a new `EventId` and defeats that.
- **`Source` and `detail-type` are derived from the event type**, never hand-written — `rs.payments`
  and `payments.<EventName>.v<N>` respectively.
- A rejected entry throws `ResourceAccessException` when the error code is transient
  (`ThrottlingException`, `InternalFailure`) and `InvalidOperationException` otherwise. In the
  recorder lambda the former routes back to the source queue for a retry and the latter to the DLQ
  (see `ProcessFailureHandler`).

## Consuming (the shape new consumers should take)

```
{env}-wayroo-events ──rule──▶ SQS queue ──event source──▶ Lambda
                               └─ DLQ
```

An SQS queue between the rule and the Lambda is what gives you retries, a DLQ and the
age-of-oldest-message alarm; `ConfigurationRecorderLambda.cs` in the infrastructure project is the
worked example of that wiring (against the webhook bus, but the rule/queue/alarm shape is identical).

Consumers must:

1. **Parse the EventBridge envelope natively.** `IntegrationJson.DeserializeMessage<TData>(body)`
   returns an `EventBridgeEnvelope<TData>` — `detail` holds the `IntegrationEnvelope<TData>`, and
   `IsReplay` tells you the event came from an archive replay.

   > ⚠️ Do **not** reach for `EventQueueDeserializer` (`RetailSuccess.WorkerService.Queues`). It has
   > no EventBridge branch: given an EventBridge body it silently deserializes into a
   > **default-valued event** instead of failing, so the bug shows up as zeroed fields downstream
   > rather than as an exception at the boundary.

2. **Dedupe on `Metadata.EventId`** (UUIDv7). At-least-once delivery with retries up to 24h makes
   this mandatory, not advisory.

3. **Reject stale writes using `Metadata.Sequence`** where the event projects into a read model —
   EventBridge does not guarantee order.

Legacy SNS topics are untouched by any of this; they retire with ProPay (D19.9).
