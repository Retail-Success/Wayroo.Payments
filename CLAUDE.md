# Coding conventions

- Prefer file-scoped namespaces (`namespace Foo.Bar;`) over block-scoped namespaces (`namespace Foo.Bar { }`) in all C# files.
- All controller action methods (once an API is added) must include a `Name` property on their HTTP verb attribute (e.g. `[HttpGet("route", Name = nameof(MyAction))]`). NSwag uses the `Name` property to generate deterministic operation IDs in the SDK client.

# Structure

- `Wayroo.Payments.ConfigurationRecorder.Lambda` — the SQS-triggered worker lambda. `Function.cs` wires up
  Serilog JSON logging and a DI service provider, and validates required env vars (declared in
  `EnvironmentVariableKeys.cs`) on cold start.
- `Wayroo.Payments.Eventing` — the local `IIntegrationEventPublisher` for this service: serializes the
  Wayroo.Common integration envelope and `PutEvents` it to the intraprocess bus. Read its `README.md`
  before writing a publisher or a consumer — notably, consumers must parse the EventBridge envelope
  natively (`IntegrationJson.DeserializeMessage`), never via `EventQueueDeserializer`.
- `Wayroo.Payments.Messages` — the provider-neutral payment integration events this service publishes
  on EventBridge (`MerchantAccountStatusChanged`, `PaymentSettlementRecorded`, `PayoutCompleted`,
  `DisputeOpened`, `DisputeStatusChanged`, `TransferReturned`), shipped as a NuGet package on the Luci
  feed. The envelope they plug into (`IStoreScopedEvent`, `IntegrationEnvelope`, `DetailType`,
  `IntegrationJson`) comes from the `Wayroo.Common` package. Two rules govern changes here, both
  documented on `PaymentEvents`:
    - **Provider neutrality.** The only provider detail allowed through is an opaque `ProviderId` plus
      `Provider*Ref` strings (and the diagnostic-only `Capabilities` bag). A provider status enum,
      error taxonomy or account model on these types is a design error — the point is that a store can
      change provider without a consumer changing.
    - **Additive-minor only.** Adding a nullable property or a new event type is safe; adding an
      **enum member is breaking**, because enums travel as strings and an unrecognised value throws in
      a consumer on an older package version. That is why the enums are complete sets up front and
      open-ended vocabularies (fee types, reason codes) are `string` with well-known constants.
      `Wayroo.Payments.Messages.UnitTests` pins both behaviours — extend it with any contract change.
- `Wayroo.Payments.Infrastructure` — the AWS CDK app that deploys the lambda (log group, SQS
  queue + DLQ, CloudWatch alarms). `Program.cs` -> `ResourceStack.cs` -> `Resources/*.cs`.

Two EventBridge buses per environment, both provisioned elsewhere and imported by ARN — this repo
creates neither. `{env}-webhook-bus` is **inbound** (provider webhooks; the ConfigurationRecorder
consumes it); `{env}-wayroo-events` is **outbound** (intraprocess events; the publisher writes to
it). Don't conflate them: publishing to the webhook bus would loop events back into the recorder's
own catch-all rule.
- `components.json` drives the CI pipeline's component discovery (lambda + infrastructure).
