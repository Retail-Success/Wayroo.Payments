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
- `Wayroo.Payments.Infrastructure` — the AWS CDK app that deploys the lambda (log group, SQS
  queue + DLQ, CloudWatch alarms). `Program.cs` -> `ResourceStack.cs` -> `Resources/*.cs`.

Two EventBridge buses per environment, both provisioned elsewhere and imported by ARN — this repo
creates neither. `{env}-webhook-bus` is **inbound** (provider webhooks; the ConfigurationRecorder
consumes it); `{env}-wayroo-events` is **outbound** (intraprocess events; the publisher writes to
it). Don't conflate them: publishing to the webhook bus would loop events back into the recorder's
own catch-all rule.
- `components.json` drives the CI pipeline's component discovery (lambda + infrastructure).
