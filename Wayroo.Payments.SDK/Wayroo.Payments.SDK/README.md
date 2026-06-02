# Wayroo.Payments.SDK

In-process client surface for the Wayroo Payments service. Consumers (e.g. Luci.Management.Api)
inject `Wayroo.Payments.SDK.Clients.IClient` to read recorded per-store payment-provider
configurations without taking a direct dependency on the data-access layer.

## Noteworthy callouts

This SDK currently delegates to `Wayroo.Payments.DataAccess` directly. Eventually Wayroo.Payments
will expose its own HTTP API and the SDK will switch to calling it (same paradigm as the rest of
the Wayroo APIs). Until then, the `Wayroo.Payments.DataAccess` dll is forced into the package
output and should be removed when the SDK switches to the HTTP API.

## Usage

```csharp
using Wayroo.Payments.SDK.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPaymentsSDK(builder.Configuration);
```

Configuration keys consumed (via `Wayroo.Payments.DataAccess`):

- `AwsRegion` (default `us-east-1`)
- `DynamoDb:ServiceUrl` (optional — for DynamoDB Local in integration tests)
- `PaymentConfigurationTableName` (default `PaymentConfiguration`)
