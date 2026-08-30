# Payment Configuration — data model docs

Visual references for the `PaymentConfiguration` DynamoDB table that the Configuration Recorder writes
to. These are design/visualization artifacts — the source of truth is the code (see
[How this maps to code](#how-this-maps-to-code)).

## Files

| File | What it is |
| --- | --- |
| [`ProviderConfiguration.json`](ProviderConfiguration.json) | An **AWS NoSQL Workbench** data-model export (format v3.0): the table's key schema, attributes, sample rows, and access patterns. Import it into NoSQL Workbench (*Import data model*) to explore or edit the design interactively. |
| [`PaymentConfigurations.png`](PaymentConfigurations.png) | A rendered snapshot of the Workbench **aggregate view** — the table with the sample rows from the model above. Handy for a quick look without opening Workbench. |

![PaymentConfigurations table — aggregate view](PaymentConfigurations.png)

> The sample values (`foo`/`bar`/`bike`, random numbers) are Workbench-generated placeholders — they
> only illustrate shape and types, not real data.

## Table at a glance

One item per **store + provider**. The partition key is the store; the sort key is the provider, so a
store's providers live together in a single partition.

| Attribute | DynamoDB type | Role | Notes |
| --- | --- | --- | --- |
| `StoreId` | Number (`N`) | **Partition key** | The store the configuration belongs to. |
| `ProviderId` | String (`S`) | **Sort key** | e.g. `propay`, `paypal`. |
| `AccountId` | String (`S`) | attribute | Provider account id; the stable identity across credential rotations. Written only when present (sparse). |
| `TenantId` | Number (`N`) | attribute | The tenant the store belongs to. |
| `ProviderConfiguration` | String (`S`) | attribute | The provider's **credential** payload (Tap to Pay / merchantware), stored verbatim as JSON. Written by the recorder. |
| `ProviderAccountDetails` | String (`S`) | attribute | The provider's **account information**, stored verbatim as JSON. Written by the account refresh. |
| `AccountStatus` | String (`S`) | attribute | The provider-neutral standing derived from the account information, by enum *name*. |
| `AccountDetailsRefreshedOn` | String (`S`) | attribute | ISO 8601; when the account information was last read from the provider. |
| `AcquiringProviderId` | String (`S`) | attribute | **Routing item only.** The provider the store's new payments go to. |
| `MigrationState` | String (`S`) | attribute | **Routing item only.** Where the store sits in the provider migration. |
| `ConfigurationVersion` | Number (`N`) | attribute | **Routing item only.** Incremented server-side on every routing write; travels as the integration event's `Sequence`. |
| `CreatedOn` | String (`S`) | attribute | ISO 8601; first time the record was written. |
| `ModifiedOn` | String (`S`) | attribute | ISO 8601; last write. |

### Three writers, one record

The record has three independent writers, and they arrive in no fixed order:

| Writer | Item | Owns |
| --- | --- | --- |
| `Wayroo.Payments.ConfigurationRecorder.Lambda` (provider webhook) | `{provider}` | `ProviderConfiguration` |
| `PaymentAccountManager.RefreshAccount` (account refresh / backfill) | `{provider}` | `ProviderAccountDetails`, `AccountStatus`, `AccountDetailsRefreshedOn` |
| A provider cutover (`UpsertRouting`) | `#routing` | `AcquiringProviderId`, `MigrationState`, `ConfigurationVersion` |

The two provider-row writers share an item, so **neither uses `PutItem`** — a whole-item put by
either would silently erase the other's attributes, taking Tap to Pay credentials or a store's
recorded standing with it. Each issues an `UpdateItem` naming only what it owns
(`PaymentConfigurationSchemaProvider.GetConfigurationUpdate` / `GetAccountDetailsUpdate`), and
`CreatedOn` is seeded with `if_not_exists` so the earlier writer keeps it. Routing sits on its own
item, so a cutover cannot disturb either — but it shares the partition, which is the sharp edge:
**anything listing a store's providers has to skip the `#routing` sort key**, or it hands back a
provider configuration that is not one.

`ConfigurationVersion` is incremented with `ADD`, never written from a value read earlier. Two
concurrent writers must not land on the same version, because consumers discard an update carrying a
version they have already applied — a duplicated one would make a real change read as stale.

Held in place by the integration tests `UpsertConfiguration_DoesNotEraseRecordedAccountDetails`,
`UpsertAccountDetails_DoesNotEraseTheCredentialPayload`,
`UpsertRouting_DisturbsNeitherTheCredentialsNorTheAccountDetails`,
`UpsertRouting_IncrementsTheVersionOnEveryWrite` and
`GetConfigurationsForStore_DoesNotReturnTheRoutingRecord`.

### Which provider is a store on?

`AcquiringProviderId` on the `#routing` item is the platform's source of truth, and Orders keeps a
read model of it (see `StoreProviderConfigChanged`). `PaymentAccountManager` resolves it in one read:
the recorded value if there is one, otherwise the configured default provider. A store with no routing
item therefore behaves exactly as it always did, which is what makes the mechanism inert and means
**no data backfill was required to turn it on**. A routing value naming a provider with no gateway
raises rather than quietly falling back.

### Item collection — one partition per store

```
StoreId = 21614               ← partition
 ├─ ProviderId = "#routing"   → AcquiringProviderId, MigrationState, ConfigurationVersion
 ├─ ProviderId = "paypal"     → AccountId, TenantId, ProviderConfiguration, CreatedOn, ModifiedOn
 └─ ProviderId = "propay"     → …
StoreId = 50327
 └─ ProviderId = "propay"     → …
StoreId = 89637
 └─ ProviderId = "paypal"     → …
```

Because providers are sorted under the store partition, "get everything for a store" is a single
partition query (no scan).

## Access patterns

Captured in the model (`AccessPatterns`):

| Name | Operation | Key condition | Returns |
| --- | --- | --- | --- |
| `UpsertConfiguration` | `UpdateItem` | `StoreId` (PK) + `ProviderId` (SK) | merges the credential attributes into one config |
| `UpsertAccountDetails` | `UpdateItem` | `StoreId` (PK) + `ProviderId` (SK) | merges the account-information attributes into one config |
| `GetRouting` | `GetItem` | `StoreId` (PK) + `#routing` (SK) | the store's routing, or nothing |
| `UpsertRouting` | `UpdateItem` | `StoreId` (PK) + `#routing` (SK) | writes routing and increments its version |
| `GetStoreProviderConfiguration` | `GetItem` | `StoreId` (PK) + `ProviderId` (SK) | one config |
| `GetAllStoreConfigurations` | `Query` | `StoreId` (PK) only | every provider config for a store |

## How this maps to code

| Concern | Source |
| --- | --- |
| Model (CLR shape) | [`../../../Wayroo.Payments.Models/Wayroo.Payments.Models/PaymentProviderConfiguration.cs`](../../../Wayroo.Payments.Models/Wayroo.Payments.Models/PaymentProviderConfiguration.cs) |
| Attribute names + (de)serialization | [`../PaymentConfigurationSchemaProvider.cs`](../PaymentConfigurationSchemaProvider.cs) |
| Access patterns (queries/puts) | [`../PaymentConfigurationRepository.cs`](../PaymentConfigurationRepository.cs) |
| Provisioned table (PK/SK, GSI, KMS) | [`../../../Wayroo.Payments.Infrastructure/Wayroo.Payments.Infrastructure/Resources/PaymentConfigurationTable.cs`](../../../Wayroo.Payments.Infrastructure/Wayroo.Payments.Infrastructure/Resources/PaymentConfigurationTable.cs) |

## Keeping the model in sync

The Workbench model is a design draft. Two things to reconcile:

- **Table name differs.** The model is named `PaymentConfigurations` (plural); the deployed table is
  `{environment}-PaymentConfiguration` (singular, env-prefixed). The deployed name is authoritative.
- **The account-information and routing attributes are newer than the model.**
  `ProviderAccountDetails`, `AccountStatus`, `AccountDetailsRefreshedOn` and the `#routing` item's
  attributes are described above but are not in the Workbench export's sample rows.

Real tables are also encrypted with a customer-managed KMS key and have point-in-time recovery enabled
(see the CDK table) — those operational settings aren't represented in the Workbench model.
