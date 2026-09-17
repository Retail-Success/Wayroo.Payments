using Amazon.DynamoDBv2.Model;
using Wayroo.Payments.DataAccess.Extensions;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.DataAccess;

/// <summary>
/// The Adyen half of the payment configuration schema: the identifiers onboarding creates, how far it
/// got, and where each capability stands.
/// </summary>
/// <remarks>
/// <para>
/// These attributes live on the store's ordinary <c>adyen</c> provider record — the same shape as the
/// ProPay record beside it, not a reserved sort key of its own. That keeps the record visible to
/// anything listing a store's providers, which is what such a listing should report.
/// </para>
/// <para>
/// <b>Capability state is flattened into top-level scalars rather than nested.</b> Two reasons, both
/// practical: an index key has to be a top-level attribute, so nesting would foreclose ever indexing
/// the fleet by readiness or grace deadline; and updates to overlapping document paths are rejected,
/// which would fight the one-writer-names-only-what-it-owns rule the rest of this record depends on.
/// </para>
/// </remarks>
public static partial class PaymentConfigurationSchemaProvider
{
    /// <summary>
    /// The sort key an Adyen account record lives under.
    /// </summary>
    /// <remarks>
    /// An ordinary provider id, not a reserved key. Must match <c>ProviderIds.Adyen</c> in
    /// Wayroo.Payments.Messages, which this project cannot reference because the models tier carries
    /// no package dependencies.
    /// </remarks>
    public const string AdyenSortKey = "adyen";

    private const string RequestedSuffix = "Requested";
    private const string EnabledSuffix = "Enabled";
    private const string AllowedSuffix = "Allowed";
    private const string StatusSuffix = "Status";
    private const string GraceUntilSuffix = "GraceUntil";
    private const string SequenceSuffix = "Sequence";

    /// <summary>Builds the PK + SK identifying a store's Adyen account record.</summary>
    /// <param name="storeId">The store.</param>
    public static Dictionary<string, AttributeValue> GetAdyenAccountIdentifiers(long storeId)
        => GetRecordIdentifiers(storeId, AdyenSortKey);

    /// <summary>The attribute holding one capability's flag or value.</summary>
    /// <param name="capability">The capability.</param>
    /// <param name="suffix">Which of its values.</param>
    public static string AttributeNameFor(AdyenCapability capability, string suffix)
        => $"{capability}{suffix}";

    /// <summary>
    /// Builds the write the onboarding ladder makes after each call to Adyen: the identifier just
    /// returned, and the step now reached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the identifiers actually supplied are written, so one rung cannot disturb another's — and
    /// nothing here touches the capability attributes, which belong to the webhook consumer.
    /// </para>
    /// <para>
    /// The version is incremented with <c>ADD</c> rather than written from a value read earlier, so
    /// two concurrent writers cannot land on the same version. It travels as the sequence on the
    /// events published from this record, where a duplicate would make a real change read as stale.
    /// </para>
    /// </remarks>
    /// <param name="account">Supplies the values; the version is assigned here.</param>
    /// <param name="now">The timestamp to stamp onto ModifiedOn, and onto CreatedOn if absent.</param>
    public static PaymentConfigurationUpdate GetAdyenOnboardingUpdate(AdyenAccount account, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.StoreId <= 0)
            throw new ArgumentException("StoreId is required on the Adyen account.", nameof(account));

        var update = BuildUpdate(
            now,
            [
                (nameof(AdyenAccount.TenantId), account.TenantId.ToAttributeValue()),
                (nameof(AdyenAccount.OwnerId), account.OwnerId.ToAttributeValue()),
                // Written only when present: a rung that has not run yet must leave the attribute
                // absent rather than null it, so the guard against overwriting one can tell them apart.
                (nameof(AdyenAccount.LegalEntityId), Optional(account.LegalEntityId)),
                (nameof(AdyenAccount.BusinessLineId), Optional(account.BusinessLineId)),
                (nameof(AdyenAccount.AccountHolderId), Optional(account.AccountHolderId)),
                (nameof(AdyenAccount.BalanceAccountId), Optional(account.BalanceAccountId)),
                (nameof(AdyenAccount.OnboardingStep), account.OnboardingStep.ToString().ToAttributeValue()),
                (nameof(AdyenAccount.OnboardingGeneration), account.OnboardingGeneration.ToAttributeValue()),
            ]);

        return WithVersionIncrement(update);
    }

    /// <summary>
    /// Builds the write a capability update makes: every capability's state, and the standing derived
    /// from them.
    /// </summary>
    /// <remarks>
    /// Writes the capabilities supplied and leaves the rest alone, so a webhook naming a subset does
    /// not erase what it did not mention. A capability with no grace period leaves the deadline
    /// attribute absent rather than null, which is what lets an index over it find only the accounts
    /// actually in grace.
    /// </remarks>
    /// <param name="account">Supplies the capability states and derived status.</param>
    /// <param name="now">The timestamp to stamp onto ModifiedOn, and onto CreatedOn if absent.</param>
    public static PaymentConfigurationUpdate GetAdyenCapabilitiesUpdate(AdyenAccount account, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.StoreId <= 0)
            throw new ArgumentException("StoreId is required on the Adyen account.", nameof(account));

        var attributes = new List<(string, AttributeValue?)>
        {
            (nameof(AdyenAccount.AccountStatus), account.AccountStatus.ToAttributeValue()),
        };

        var removals = new List<string>();

        foreach (var (capability, state) in account.Capabilities)
        {
            attributes.Add((AttributeNameFor(capability, RequestedSuffix), state.Requested.ToAttributeValue()));
            attributes.Add((AttributeNameFor(capability, EnabledSuffix), state.Enabled.ToAttributeValue()));
            attributes.Add((AttributeNameFor(capability, AllowedSuffix), state.Allowed.ToAttributeValue()));
            attributes.Add((AttributeNameFor(capability, StatusSuffix), Optional(state.VerificationStatus)));
            attributes.Add((AttributeNameFor(capability, SequenceSuffix), state.LastEventSequence.ToAttributeValue()));

            var graceAttribute = AttributeNameFor(capability, GraceUntilSuffix);
            if (state.GraceUntil.HasValue)
            {
                attributes.Add((graceAttribute, state.GraceUntil.Value.ToAttributeValue()));
            }
            else
            {
                // A grace period that has ended is removed, not nulled: an attribute set to null still
                // exists, and would keep the account in any index built over this one.
                removals.Add(graceAttribute);
            }
        }

        var update = WithVersionIncrement(BuildUpdate(now, attributes));

        return removals.Count == 0 ? update : WithRemovals(update, removals);
    }

    /// <summary>Reconstructs an Adyen account record from a DynamoDB item.</summary>
    /// <param name="attributes">The item.</param>
    public static AdyenAccount GetAdyenAccountModel(Dictionary<string, AttributeValue> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var account = new AdyenAccount
        {
            StoreId = attributes.GetLong(AttributeNameForPartitionKey) ?? 0,
            TenantId = attributes.GetLong(nameof(AdyenAccount.TenantId)),
            OwnerId = attributes.GetGuid(nameof(AdyenAccount.OwnerId)),
            LegalEntityId = attributes.GetString(nameof(AdyenAccount.LegalEntityId)),
            BusinessLineId = attributes.GetString(nameof(AdyenAccount.BusinessLineId)),
            AccountHolderId = attributes.GetString(nameof(AdyenAccount.AccountHolderId)),
            BalanceAccountId = attributes.GetString(nameof(AdyenAccount.BalanceAccountId)),
            OnboardingStep = attributes.GetAdyenOnboardingStep(nameof(AdyenAccount.OnboardingStep)),
            // A record written before generations existed is the first attempt, which is what a
            // missing attribute has to read as for its idempotency keys to stay reproducible.
            OnboardingGeneration = attributes.GetLong(nameof(AdyenAccount.OnboardingGeneration)) ?? 1,
            AccountStatus = attributes.GetPaymentAccountStatus(nameof(AdyenAccount.AccountStatus)),
            AggregateVersion = attributes.GetLong(nameof(AdyenAccount.AggregateVersion)) ?? 0,
            CreatedOn = attributes.GetDateTimeOffset(nameof(AdyenAccount.CreatedOn)),
            ModifiedOn = attributes.GetDateTimeOffset(nameof(AdyenAccount.ModifiedOn)),
        };

        foreach (var capability in AdyenCapabilities.All)
        {
            var requested = attributes.GetBool(AttributeNameFor(capability, RequestedSuffix));
            var enabled = attributes.GetBool(AttributeNameFor(capability, EnabledSuffix));
            var allowed = attributes.GetBool(AttributeNameFor(capability, AllowedSuffix));
            var status = attributes.GetString(AttributeNameFor(capability, StatusSuffix));
            var grace = attributes.GetDateTimeOffset(AttributeNameFor(capability, GraceUntilSuffix));
            var sequence = attributes.GetLong(AttributeNameFor(capability, SequenceSuffix));

            // A capability nothing has ever reported on is absent rather than present-and-false, so a
            // caller can tell "not yet told" from "told, and refused".
            if (requested is null && enabled is null && allowed is null && status is null)
                continue;

            account.Capabilities[capability] = new AdyenCapabilityState
            {
                Requested = requested ?? false,
                Enabled = enabled ?? false,
                Allowed = allowed ?? false,
                VerificationStatus = status,
                GraceUntil = grace,
                LastEventSequence = sequence,
            };
        }

        return account;
    }

    private static AttributeValue? Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.ToAttributeValue();

    private static PaymentConfigurationUpdate WithVersionIncrement(PaymentConfigurationUpdate update)
    {
        const string version = nameof(AdyenAccount.AggregateVersion);

        update.ExpressionAttributeNames[$"#{version}"] = version;
        update.ExpressionAttributeValues[":versionIncrement"] = 1L.ToAttributeValue();

        return update with
        {
            UpdateExpression = $"{update.UpdateExpression} ADD #{version} :versionIncrement",
        };
    }

    private static PaymentConfigurationUpdate WithRemovals(
        PaymentConfigurationUpdate update,
        IReadOnlyList<string> attributeNames)
    {
        var placeholders = new List<string>(attributeNames.Count);

        foreach (var attributeName in attributeNames)
        {
            update.ExpressionAttributeNames[$"#{attributeName}"] = attributeName;
            placeholders.Add($"#{attributeName}");
        }

        return update with
        {
            UpdateExpression = $"{update.UpdateExpression} REMOVE {string.Join(", ", placeholders)}",
        };
    }
}
