# Wayroo.Payments.SDK

Typed HTTP client for the Wayroo Payments microservice. Composites (e.g. Luci.Management.Api) inject
`Wayroo.Payments.SDK.Clients.IClient` to read recorded per-store payment-provider configurations
without coupling to the micro's persistence layer.

## Usage

```csharp
using Wayroo.Payments.SDK.Extensions;

services.AddWayrooPaymentsClient(
    builder.Configuration.BindSectionByTypeName,
    n => n.AddHttpMessageHandler<HttpClientXRayTracingHandler>());
```

`WayrooPaymentsClientOptions` (section name: `WayrooPaymentsClientOptions`):

```json
{
  "ApiBaseUrl": "https://payments.wayroo-dev"
}
```

## Calling the API

```csharp
public class MyService(IClient payments)
{
    public Task<IReadOnlyList<PaymentProviderConfiguration>> Load(long storeId, CancellationToken ct)
        => payments.GetConfigurationsForStore(storeId, ct);
}
```

The single-record `GetConfiguration` throws `Refit.ApiException` with `StatusCode == NotFound` when
no configuration has been recorded for the store + provider — catch and treat as null on the consumer
side if a null-on-miss surface is wanted.

## Merchant accounts

`GetAccountBalance` is the provider-neutral replacement for the Orders SDK's
`IStorePropayClient.GetPropayAccountBalanceAsync`:

```csharp
var balance = await payments.GetAccountBalance(tenantId, storeId, cancellationToken: ct);

if (!balance.AccountExists)
    return NoMerchantAccount();

if (balance.Status == PaymentAccountStatus.ReadyToProcess && balance.AvailableBalance!.Amount >= amount)
    return Charge();
```

Differences from the Orders endpoint worth knowing when migrating:

- **No 404 on a missing account.** A store that never onboarded comes back `200` with
  `accountExists: false`, matching the Orders behaviour — branch on the flag, don't catch.
- **Amounts are `MoneyAmount`, not bare decimals**, and always in major units (dollars).
- **`Status` is the neutral `PaymentAccountStatus`**, not ProPay's 24-value enum. It is never null
  when `accountExists` is true. Branch on it and on `CanProcessPayments` / `CanReceivePayouts`, never
  on a provider's own vocabulary.
- **A provider refusal is a `400`**, surfaced by Refit as an `ApiException` carrying
  `ValidationProblemDetails` — the same shape Orders returned. A `409` means the service cannot tell
  which provider the store is on and an operator has to settle it; retrying will not help.
- **You do not name the provider.** Which one a store transacts through is the service's job to work
  out, so that a store moving between providers changes nothing here. `providerId` remains as an
  optional override for support ("what does the old provider still say about this store?").

`RefreshAccount` re-reads a store's account information from the provider and records it. It is the
per-store unit of work a payment-account backfill drives:

```csharp
// For a store the service already knows about:
var details = await payments.RefreshAccount(tenantId, storeId, cancellationToken: ct);

// Seeding a store it has no record of — a backfill supplies the reference, because the service
// never calls another Wayroo service to find one:
var seeded = await payments.RefreshAccount(
    tenantId,
    storeId,
    new RefreshPaymentAccountRequest { ProviderAccountRef = "718040110898" },
    cancellationToken: ct);
```

It is safe to repeat, and it only writes the account-information attributes — the provider credentials
recorded from webhooks are left untouched. Note it calls the provider on every invocation, so a sweep
across many stores needs its own concurrency cap; nothing rate-limits it server-side.

A store the service holds no account reference for comes back `accountExists: false` unless the caller
supplies `providerAccountRef`. That is deliberate: Wayroo.Payments does not call other Wayroo services,
so it cannot go and look one up. Whoever drives a backfill already has the references and passes them in.

## Notes

- The micro is unauthenticated; auth is enforced upstream by the composite. The SDK does not attach
  bearer tokens.
- `StoreId` flows as a route parameter, not a JWT claim.
- The account routes also carry `tenantId`, unlike the configuration routes. That is not decoration:
  the provider selects its credentials by tenant, and a store this service has no record of yet cannot
  tell us which tenant it belongs to.
- This client is hand-written, not generated. A new endpoint means hand-adding the method and its
  literal route here.
