# What D6a hands you

D6a is done and in review ([PR #27](https://github.com/Retail-Success/Wayroo.Payments/pull/27)). It is
the only Adyen story that calls Adyen's account APIs **outbound** — everything after it reacts to what
it created. This is what it leaves behind for you to build on.

Written for the downstream Adyen stories (D6b, D7a/b, D8, D13). If something you need isn't here, it
probably isn't built yet — the last section says what's deliberately left.

---

## 1. The short version

D6a opens four Adyen objects for a store and writes their identifiers down:

```
legal entity  →  business line  →  account holder  →  balance account
 (who they are)  (what they sell)   (what they may do)  (where money lands)
```

Everything Adyen tells us afterwards — verification progressed, a capability became usable, a payout
moved — arrives as a **webhook about one of those four objects**. Your job starts there.

**The record is the contract.** There's no API you call to find out where a store stands; you read and
write the same DynamoDB item D6a wrote.

---

## 2. Where the state lives

Table `{env}-PaymentConfiguration`, which already holds the ProPay records.

| | |
| --- | --- |
| **Partition key** | `StoreId` (Number) |
| **Sort key** | `ProviderId` (String) — the Adyen record is `"adyen"` |

The store's Adyen record sits beside its `propay` record and its `#routing` record in the same
partition. **Anything listing a store's providers must skip `#routing`.**

### What's on the `adyen` item

| Attribute | Type | Written by | Meaning |
| --- | --- | --- | --- |
| `StoreId` | N | — | partition key |
| `ProviderId` | S | — | `"adyen"` |
| `TenantId` | N | D6a | the tenant the store sells for |
| `OwnerId` | S | D6a | our person id — **always absent today**, see §5 |
| `LegalEntityId` | S | D6a | the seller as a person |
| `BusinessLineId` | S | D6a | what they sell, per tenant |
| `AccountHolderId` | S | D6a | what they're permitted to do — **capabilities hang off this** |
| `BalanceAccountId` | S | D6a | where this store's earnings land |
| `OnboardingStep` | S | D6a | how far the ladder got — see §3 |
| `OnboardingGeneration` | N | D6a | attempt number, starts at 1 |
| `AccountStatus` | S | **you** | derived standing |
| `{Capability}{Field}` | various | **you** | capability state, flattened — see below |
| `AggregateVersion` | N | both | incremented by the database on every write |
| `CreatedOn` / `ModifiedOn` | S | both | |

### Capability attributes are flat, not nested

Five capabilities × six fields, as **top-level** attributes named `{Capability}{Field}`:

```
ReceivePaymentsRequested      ReceivePaymentsEnabled     ReceivePaymentsAllowed
ReceivePaymentsStatus         ReceivePaymentsGraceUntil  ReceivePaymentsSequence
```

…and the same for `ReceiveFromPlatformPayments`, `SendToTransferInstrument`,
`SendToBalanceAccount`, `ReceiveFromBalanceAccount`.

Flat for two reasons, both of which matter to you: **an index key has to be a top-level attribute**
(so the fleet can later be indexed by readiness or grace deadline), and **DynamoDB rejects updates to
overlapping document paths** — which would break the rule below.

### The one rule that keeps this safe

> **Every writer names only the attributes it owns.** All writers use `UpdateItem`, never `PutItem`.

Three writers share this item and land in no fixed order: the webhook recorder owns the credential
payload, the account refresh owns `ProviderAccountDetails`/`AccountStatus`, and onboarding owns the
identifiers. A whole-item `PutItem` by any of them erases the others' work.

---

## 3. `OnboardingStep` — what D6a controls, and where it stops

```
NotStarted → LegalEntityCreated → BusinessLineCreated → AccountHolderCreated → Complete
```

Stored **by name**, not number, so inserting a step can't silently reinterpret existing rows.

**`Complete` means every object exists. It does not mean the store can be paid.** At that point the
capabilities have been *requested* and nothing more. Whether they become usable is decided by the
seller finishing verification — which is reported over webhooks, and is where your work begins.

D6a never writes `AccountStatus` and never writes a capability attribute. Those are yours.

---

## 4. How to tell which store a webhook is about

**This is the part worth reading twice.**

Adyen account webhooks carry **no merchant account and no store id**. The payload for
`balancePlatform.accountHolder.updated` gives you:

```
balancePlatform     one value, identical for every tenant — useless for routing
accountHolder.id    an Adyen id we didn't choose
legalEntityId       an Adyen id we didn't choose
reference           ← ours
metadata            empty today
capabilities, status, verificationDeadlines
```

**The `reference` is the only field that says whose it is.** D6a writes one on every object at
creation — it's the only chance, Adyen won't let you set it later — and Adyen echoes it back on every
webhook.

### Reading a reference

Format: `<env>[<scope><id>][_<subject>][_<type>]`

```
^([DQP])(?:(t\d+)|(p[0-9a-f]{32}))?(?:_(s\d+))?(?:_(le|ah|ba|st|ti|sc|ma))?$
```

`Dt000004_s0031610_ah` → Dev, tenant 4, store 31610, account holder. That's a direct `GetItem` on the
partition key. **No index, no scan, strongly consistent.**

**Use `AdyenReference.TryParse` in `Wayroo.Payments.Models`. Don't hand-roll it and never string-compare
two references** — it parses either separator and any digit width, and compares on values, because both
spellings exist in the wild.

Three things that will bite if you don't know them:

- **The separator is `_`, not `:`.** A colon is fine on a legal entity but rejected outright by a store
  and a merchant account, so one format has to take the intersection. Our *internal* Frontegg store ids
  keep their colons — only the value sent to Adyen is transformed.
- **Business lines carry no reference at all.** `BusinessLineInfo` has no such field. A business line
  resolves only through `BusinessLineId` on our record.
- **Transfer/payout webhooks carry it too**, on `TransferData.AccountHolder` and `.BalanceAccount`,
  which are `ResourceReference` objects with both `Id` and `Reference`.

---

## 5. Three things that are deliberately not what they look like

**`OwnerId` is always absent.** The platform has no person identity — no table, and not the Frontegg
user either, which mints a new user per signup. So today one legal entity is created per store, and a
seller's legal entity and account holder are **store-scoped** (`Pt000004_s0031610_le`). The field and
the person scope exist so that when a real person id lands, reuse switches on with no re-onboarding.
**Don't build anything that assumes one account holder serves one store forever — but don't build for
multi-store yet either.** It's unresolved (see §7).

**The capability flags are independent, not a progression.** A freshly created account holder comes
back `requested: true`, `enabled: true`, `allowed: false`, `verificationStatus: pending` — all at once.
Only `Allowed` **and** `verificationStatus == "valid"` means usable. `AdyenCapabilityState.IsUsable`
encodes exactly that.

**`receiveFromPlatformPayments` is the one that costs money when it's missing.** Without it a split
books to the platform's liable balance account instead of the seller's — **and nothing errors**. It
surfaces at reconciliation. `AdyenCapabilities.RequiredToProcessPayments` is the set that must all be
usable before a store may be routed real money.

---

## 6. The write API built for you

`IAdyenAccountRepository` in `Wayroo.Payments.Models`, implemented in `Wayroo.Payments.DataAccess`:

```csharp
Task<AdyenAccount?> GetAdyenAccount(long storeId, CancellationToken ct);
Task<AdyenAccount>  RecordOnboardingProgress(AdyenAccount account, CancellationToken ct);
Task<AdyenAccountWriteResult> RecordCapabilities(AdyenAccount account, CancellationToken ct);
```

**`RecordCapabilities` exists for you — D6a never calls it.** Two things it does deliberately:

- It returns `AdyenAccountWriteResult { Current, Previous }` from **one** write, with a `StatusChanged`
  convenience. That's so a status change can be published as a *transition* rather than a level,
  without a second and racy read. `MerchantAccountStatusChanged` has both `Status` and `PreviousStatus`
  for exactly this.
- It writes the capabilities you supply and **leaves the rest alone**, so a webhook naming a subset
  can't erase what it didn't mention. A capability with no grace period has its deadline attribute
  **removed**, not nulled — an attribute set to null still exists and would keep the account in any
  index built over it.

`GetAdyenAccount` is a **strongly consistent** read. That matters more than it looks: an
eventually-consistent read that missed an identifier written moments ago would create a second Adyen
object, and a duplicate legal entity **cannot be deleted**.

### Ordering

`AdyenCapabilityState.LastEventSequence` is there because **capability webhooks arrive at least once
and out of order**. An update carrying a sequence at or below the stored one is stale and must be
discarded, not applied. D6a doesn't populate it; the first consumer to apply an event should.

`AggregateVersion` is incremented by the database (`ADD`, not a read-then-write), so two concurrent
writers can't land on the same version. It's designed to travel as the `Sequence` on events published
from the record.

---

## 7. Open questions that could change your design

| Question | Owner | Why it matters to you |
| --- | --- | --- |
| **Can one legal entity back multiple account holders?** | Adyen | Decides whether an account holder is per person or per tenant relationship. If per person, one capability webhook has to fan out to N store records and capability state is duplicated per store. Today it's 1:1 so nothing shows. |
| What does a tenant get to see of its sellers? | product + Adyen | May constrain the account holder structure above. |
| Who absorbs a chargeback after the seller is paid? | finance + legal | Encoded in the split configuration profile, not here. |

---

## 8. What is NOT built

- **No webhook ingress, consumer, or HMAC verification** for Adyen. Nothing writes capability state today.
- **No events published** from the Adyen record. `MerchantAccountStatusChanged` exists in
  `Wayroo.Payments.Messages` and is wired for ProPay; nothing publishes it from Adyen.
- **No `AccountStatus` derivation** from capabilities.
- **No transfer/payout handling**, no sweeps.
- **`OwnerId` never populated**, no GSI on it.

## 9. Running it

**Adyen ships switched off.** `Adyen:Enabled` is `false` by default; while it's off nothing Adyen is
registered and the onboarding endpoints refuse as an unsupported provider. Turning it on needs
credentials in Parameter Store at `/luci/{env}/vendors/adyen` — **not provisioned yet**, and the
missing API credential permissions are an open ask with Adyen.

So you can build and unit-test against the record and the models today, but you can't yet make a real
seller account appear.

### Endpoints, for reference

```
POST /api/payments/v1.0/tenants/{tenantId}/stores/{storeId}/adyen/onboarding
GET  /api/payments/v1.0/tenants/{tenantId}/stores/{storeId}/adyen/onboarding/link
```

The first is safe to call repeatedly — each rung is skipped if its identifier is already recorded, so
a fully onboarded store makes no calls to Adyen at all. The second is a `302` to Adyen's hosted
onboarding, `404` if the store has no legal entity yet.

Both are also on the SDK: `OnboardStore` and `GetAdyenOnboardingLink` in `Wayroo.Payments.SDK`.

---

## Where to look in the code

| | |
| --- | --- |
| Record + models | `Wayroo.Payments.Models/Adyen*.cs` |
| Reference parsing | `Wayroo.Payments.Models/AdyenReference.cs` |
| Persistence | `Wayroo.Payments.DataAccess/AdyenAccountRepository.cs`, `PaymentConfigurationSchemaProvider.Adyen.cs` |
| Adyen calls | `Wayroo.Payments.BusinessLogic/Gateways/Adyen/` |
| The ladder | `Wayroo.Payments.BusinessLogic/Managers/AdyenOnboardingManager.cs` |

Questions → Andrew.
