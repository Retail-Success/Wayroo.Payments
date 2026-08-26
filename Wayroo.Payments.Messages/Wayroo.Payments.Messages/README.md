# Wayroo.Payments.Messages

The provider-neutral payment integration events published by the Wayroo Payments
service on EventBridge. They describe money movement and merchant standing in
terms the platform owns, so a consumer never has to know which payment provider
produced the fact — which is what lets a store move between providers without
any downstream service changing.

Targets **.NET 8** and **.NET 10**. Published to the private Azure DevOps
**Luci** feed.

```
dotnet add package Wayroo.Payments.Messages
```

Your `nuget.config` must include the Luci feed
(`https://pkgs.dev.azure.com/RetailSuccess/Luci-Dev/_packaging/Luci/nuget/v3/index.json`)
and you need Azure Artifacts authentication (e.g. the Azure Artifacts Credential
Provider).

## Events

Everything lives in the `Wayroo.Payments.Messages` namespace.

| Event | Published when |
|---|---|
| `MerchantAccountStatusChanged` | A store's merchant account changes standing — onboarding finished, something is needed from the merchant, suspended, rejected, closed |
| `PaymentSettlementRecorded` | A captured payment settles, with the fees actually charged |
| `PayoutCompleted` | Funds leave a store's balance for the merchant's bank |
| `DisputeOpened` | A cardholder disputes a payment |
| `DisputeStatusChanged` | An open dispute is defended, accepted, won, lost, expired or withdrawn |
| `TransferReturned` | A payout the merchant's bank rejected, or a funding transfer pulled back |

All six are **store-scoped** (`IStoreScopedEvent`) and publish under the
`rs.payments` source, so they route as `payments.PayoutCompleted.v1` and so on.
The tenant and store live in `Metadata.Scope`, not on the payload.

The envelope, `IntegrationJson` and `DetailType` come from
[`Wayroo.Common`](https://github.com/Retail-Success/Wayroo.Common)
(`Wayroo.Common.Models.Events`), which this package depends on.

## Provider neutrality

The only provider detail that crosses the seam is opaque: a `ProviderId` naming
the provider (`ProviderIds.Adyen`, `ProviderIds.Propay`) and `Provider*Ref`
strings that mean something only when handed back to that provider. Branch on
the neutral fields, never on `ProviderId` — if a handler needs
`if (providerId == ...)` to decide what a fact *means*, the contract is missing
a neutral field and should gain one.

```csharp
using Wayroo.Common.Models.Events;
using Wayroo.Payments.Messages;

var message = IntegrationJson.DeserializeMessage<MerchantAccountStatusChanged>(sqsMessage.Body);
MerchantAccountStatusChanged status = message.Detail.Data;

if (status.Status == MerchantAccountStatus.ReadyToProcess)
{
    await stores.EnableAsync(
        message.Detail.Metadata.Scope.RequireStoreId(), status.ProviderAccountRef, ct);
}
```

`Status` says where an account is in its lifecycle; **`CanProcessPayments` says
whether the store can sell right now**. The two come apart during a grace
period, where an account is `ActionRequired` and still processing until
`ActionRequiredBy` — which is why it is an explicit field rather than something
to infer. Money always travels as `Money` (a `decimal` amount plus its ISO 4217
currency), never as a bare number.

## Versioning policy — additive-minor only

Until the provider-decommission epic, every change here must be backward
compatible for consumers already deployed against an earlier package version:

- **Allowed:** adding an optional (nullable) property, or a whole new event
  type. Unknown properties are ignored on read, so an older consumer keeps
  working.
- **Breaking, needs a new major:** adding, removing or renaming an **enum
  member**. Enums travel as strings and an unrecognised string throws, so a
  value an older consumer has never heard of poisons every affected message.
  That is why the enums are declared as complete sets up front, and why
  anything genuinely open-ended — fee types, reason codes, capability names —
  is a `string` with well-known constants instead.
- **Breaking, needs a new major:** removing or renaming a property, changing
  its type, or making an optional property required.

A major version is a new CLR type (`PayoutCompletedV2` with `Version => 2`)
published in parallel until every consumer has moved. Because the major version
is part of the routed `detail-type`, a consumer subscribed to
`payments.PayoutCompleted.v1` never receives a `.v2` it did not opt into.

`Wayroo.Payments.Messages.UnitTests` pins both the round-trip shape and the
detail-types; extend it alongside any contract change.
