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

## Notes

- The micro is unauthenticated; auth is enforced upstream by the composite. The SDK does not attach
  bearer tokens.
- `StoreId` flows as a route parameter, not a JWT claim.
