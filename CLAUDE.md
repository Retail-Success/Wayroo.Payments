# Coding conventions

- Prefer file-scoped namespaces (`namespace Foo.Bar;`) over block-scoped namespaces (`namespace Foo.Bar { }`) in all C# files.
- All controller action methods must include a `Name` property on their HTTP verb attribute (e.g.
  `[HttpGet("route", Name = nameof(MyAction))]`), so operation IDs stay deterministic. Note the SDK is
  **hand-written Refit**, not generated — adding an endpoint means hand-adding the matching method and
  literal route to `Wayroo.Payments.SDK/Clients/IClient.cs`; nothing generates it for you.

# Structure

- `Wayroo.Payments.API` — the read API (ECS Fargate, `payments.luci-{env}`, net10). Unauthenticated: it
  trusts the calling composite. Two controllers — recorded provider configurations, and merchant
  accounts (balance + account refresh). Controllers are HTTP only — provider selection and the
  provider calls themselves live in `Wayroo.Payments.BusinessLogic`, so adding a provider never touches
  a controller. Adding a required config value means adding it to `EnvironmentVariableKeys.cs` **and** to
  the `Environment` dictionary in `Wayroo.Payments.Infrastructure/Resources/PaymentsAPI.cs`, or the API
  fails startup validation in ECS.
- **No direct dependency on another Wayroo service.** This service talks to its payment providers, its
  own DynamoDB table, and the event buses — never to Luci.Orders or any sibling API. Anything it has
  not been told, it does not go and fetch: a store whose provider account reference it holds no record
  of is reported as having no account, and a backfill supplies the reference on the refresh call
  instead. (`Wayroo.Payments.ConfigurationRecorder.Lambda` still breaks this rule — see its
  `Gateways/Propay/PropayStoreOwnerResolver`, which calls the Orders API to map an account to a store.)
- `Wayroo.Payments.BusinessLogic` — provider selection and the account operations. `IPaymentAccountManager`
  answers "which provider is this store on" so no caller has to, from the store's `#routing` record —
  `AcquiringProviderId`, falling back to `PaymentGatewayOptions.DefaultProviderId` when there is none,
  which is what keeps the mechanism inert. A routing value with no gateway raises rather than quietly
  falling back to the old provider. Gateways live behind
  `IPaymentGatewayRegistry`; adding a provider is a new `Gateways/{Provider}/` folder plus a call in
  `AddPaymentsBusinessLogic` — the registry picks it up, and **no controller changes**. This is the tier
  a backfill worker or the recorder lambda should consume rather than re-deriving the choice.
- `Wayroo.Payments.Models` — the API contracts plus `IPaymentConfigurationRepository`. Deliberately
  carries **no package references**, which is why `MoneyAmount` and `PaymentAccountStatus` are declared
  here rather than reused from `Wayroo.Payments.Messages`. `PaymentAccountStatusParityTests` fails the
  build if the two neutral status enums drift apart.
- `Wayroo.Payments.DataAccess` — DynamoDB access for `{env}-PaymentConfiguration`. **All three writers
  use `UpdateItem`, never `PutItem`**: the webhook recorder owns `ProviderConfiguration` and the account
  refresh owns `ProviderAccountDetails`/`AccountStatus`, they land on the same record in no fixed
  order, and a whole-item put by either would erase the other's work. The store's routing lives on its
  own `#routing` item in the same partition — **anything listing a store's providers must skip that
  sort key**. See `Docs/README.md`.
- `Wayroo.Payments.ConfigurationRecorder.Lambda` — the SQS-triggered worker lambda. `Function.cs` wires up
  Serilog JSON logging and a DI service provider, and validates required env vars (declared in
  `EnvironmentVariableKeys.cs`) on cold start.
- `Wayroo.Payments.Eventing` — the local `IIntegrationEventPublisher` for this service: serializes the
  Wayroo.Common integration envelope and `PutEvents` it to the intraprocess bus. Read its `README.md`
  before writing a publisher or a consumer — notably, consumers must parse the EventBridge envelope
  natively (`IntegrationJson.DeserializeMessage`), never via `EventQueueDeserializer`.
- `Wayroo.Payments.Messages` — the provider-neutral payment integration events this service publishes
  on EventBridge (`MerchantAccountStatusChanged`, `PaymentSettlementRecorded`, `PayoutCompleted`,
  `DisputeOpened`, `DisputeStatusChanged`, `TransferReturned`, `StoreProviderConfigChanged`), shipped
  as a NuGet package on the Luci feed. `StoreProviderConfigChanged` is the config-sync event Orders
  builds its provider-routing read model from (WR-19269/D5); the recorder publishes it when a store's
  routing actually changes, and its `Sequence` is the routing record's `ConfigurationVersion`. The envelope they plug into (`IStoreScopedEvent`, `IntegrationEnvelope`, `DetailType`,
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
