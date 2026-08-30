using Amazon.DynamoDBv2.Model;
using Wayroo.Payments.DataAccess.Extensions;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.DataAccess;

/// <summary>
/// Maps between <see cref="PaymentProviderConfiguration"/> and the DynamoDB attribute representation
/// of the payment configuration table. Mirrors the schema-provider pattern in
/// Wayroo.ContentLibrary.DataAccess (DSOContentSchemaProvider).
/// </summary>
public static class PaymentConfigurationSchemaProvider
{
    /// <summary>The table's partition key is the StoreId attribute (a number).</summary>
    public static string AttributeNameForPartitionKey => nameof(PaymentProviderConfiguration.StoreId);

    /// <summary>The table's sort key is the ProviderId attribute.</summary>
    public static string AttributeNameForSortKey => nameof(PaymentProviderConfiguration.ProviderId);

    /// <summary>
    /// Builds the PK + SK key dictionary used to identify a single configuration record.
    /// </summary>
    public static Dictionary<string, AttributeValue> GetRecordIdentifiers(long storeId, string providerId)
        => new()
        {
            { AttributeNameForPartitionKey, storeId.ToAttributeValue() },
            { AttributeNameForSortKey, providerId.ToAttributeValue() },
        };

    /// <summary>
    /// Builds the partition-key-only condition value used to query every record for a store.
    /// </summary>
    public static AttributeValue GetPartitionKeyValue(long storeId) => storeId.ToAttributeValue();

    /// <summary>
    /// Reconstructs a configuration from a DynamoDB item.
    /// </summary>
    public static PaymentProviderConfiguration GetModel(Dictionary<string, AttributeValue> attributes)
        => new()
        {
            StoreId = attributes.GetLong(AttributeNameForPartitionKey) ?? 0,
            ProviderId = attributes.GetString(AttributeNameForSortKey) ?? string.Empty,
            AccountId = attributes.GetString(nameof(PaymentProviderConfiguration.AccountId)),
            TenantId = attributes.GetLong(nameof(PaymentProviderConfiguration.TenantId)),
            ProviderConfiguration = attributes.GetString(nameof(PaymentProviderConfiguration.ProviderConfiguration)),
            ProviderAccountDetails = attributes.GetString(nameof(PaymentProviderConfiguration.ProviderAccountDetails)),
            AccountStatus = attributes.GetPaymentAccountStatus(nameof(PaymentProviderConfiguration.AccountStatus)),
            AccountDetailsRefreshedOn = attributes.GetDateTimeOffset(nameof(PaymentProviderConfiguration.AccountDetailsRefreshedOn)),
            CreatedOn = attributes.GetDateTimeOffset(nameof(PaymentProviderConfiguration.CreatedOn)),
            ModifiedOn = attributes.GetDateTimeOffset(nameof(PaymentProviderConfiguration.ModifiedOn)),
        };

    /// <summary>
    /// Builds the partial write owned by the configuration recorder: the account and tenant
    /// identifiers plus the provider's credential payload.
    /// </summary>
    /// <remarks>
    /// Deliberately does not name the account-detail attributes, so a credential webhook arriving
    /// after an account refresh leaves the recorded account information intact.
    /// </remarks>
    /// <param name="configuration">The configuration to write. Supplies the values, not the keys.</param>
    /// <param name="now">The timestamp to stamp onto ModifiedOn, and onto CreatedOn if absent.</param>
    public static PaymentConfigurationUpdate GetConfigurationUpdate(
        PaymentProviderConfiguration configuration,
        DateTimeOffset now)
    {
        ValidateKeys(configuration);

        return BuildUpdate(
            now,
            [
                // AccountId is sparse: only written when we actually have one, never nulled out.
                (nameof(PaymentProviderConfiguration.AccountId),
                    string.IsNullOrWhiteSpace(configuration.AccountId)
                        ? null
                        : configuration.AccountId.ToAttributeValue()),
                (nameof(PaymentProviderConfiguration.TenantId), configuration.TenantId.ToAttributeValue()),
                (nameof(PaymentProviderConfiguration.ProviderConfiguration), configuration.ProviderConfiguration.ToAttributeValue()),
            ]);
    }

    /// <summary>
    /// Builds the partial write owned by the account refresh: the provider's account information and
    /// the neutral status derived from it, plus the account and tenant identifiers.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="GetConfigurationUpdate"/> — it never names
    /// <see cref="PaymentProviderConfiguration.ProviderConfiguration"/>, so a refresh cannot discard
    /// the credential payload.
    /// </remarks>
    /// <param name="configuration">The configuration to write. Supplies the values, not the keys.</param>
    /// <param name="now">The timestamp to stamp onto ModifiedOn, and onto CreatedOn if absent.</param>
    public static PaymentConfigurationUpdate GetAccountDetailsUpdate(
        PaymentProviderConfiguration configuration,
        DateTimeOffset now)
    {
        ValidateKeys(configuration);

        return BuildUpdate(
            now,
            [
                (nameof(PaymentProviderConfiguration.AccountId),
                    string.IsNullOrWhiteSpace(configuration.AccountId)
                        ? null
                        : configuration.AccountId.ToAttributeValue()),
                (nameof(PaymentProviderConfiguration.TenantId), configuration.TenantId.ToAttributeValue()),
                (nameof(PaymentProviderConfiguration.ProviderAccountDetails), configuration.ProviderAccountDetails.ToAttributeValue()),
                (nameof(PaymentProviderConfiguration.AccountStatus), configuration.AccountStatus.ToAttributeValue()),
                (nameof(PaymentProviderConfiguration.AccountDetailsRefreshedOn),
                    (configuration.AccountDetailsRefreshedOn ?? now).ToAttributeValue()),
            ]);
    }

    private static void ValidateKeys(PaymentProviderConfiguration configuration)
    {
        if (configuration.StoreId <= 0)
            throw new ArgumentException("StoreId is required on the configuration.", nameof(configuration));

        if (string.IsNullOrWhiteSpace(configuration.ProviderId))
            throw new ArgumentException("ProviderId is required on the configuration.", nameof(configuration));
    }

    /// <summary>
    /// Assembles a SET expression over the supplied attributes. A <c>null</c> value means "this
    /// writer has nothing to say about that attribute" and omits it entirely, leaving whatever is
    /// already stored. CreatedOn is written only on first insert; ModifiedOn on every write.
    /// </summary>
    private static PaymentConfigurationUpdate BuildUpdate(
        DateTimeOffset now,
        IReadOnlyList<(string AttributeName, AttributeValue? Value)> attributes)
    {
        var names = new Dictionary<string, string>();
        var values = new Dictionary<string, AttributeValue>();
        var assignments = new List<string>();

        foreach (var (attributeName, value) in attributes)
        {
            if (value is null)
                continue;

            names[$"#{attributeName}"] = attributeName;
            values[$":{attributeName}"] = value;
            assignments.Add($"#{attributeName} = :{attributeName}");
        }

        const string createdOn = nameof(PaymentProviderConfiguration.CreatedOn);
        const string modifiedOn = nameof(PaymentProviderConfiguration.ModifiedOn);

        names[$"#{createdOn}"] = createdOn;
        names[$"#{modifiedOn}"] = modifiedOn;
        values[$":{modifiedOn}"] = now.ToAttributeValue();

        assignments.Add($"#{createdOn} = if_not_exists(#{createdOn}, :{modifiedOn})");
        assignments.Add($"#{modifiedOn} = :{modifiedOn}");

        return new PaymentConfigurationUpdate(
            $"SET {string.Join(", ", assignments)}",
            names,
            values);
    }

    /// <summary>
    /// The sort key the store-level routing record lives under.
    /// </summary>
    /// <remarks>
    /// Prefixed so it can never collide with a provider id, which is what every other item in the
    /// partition is keyed by. Anything reading the partition as a list of provider configurations has
    /// to skip it — see <c>PaymentConfigurationRepository.GetConfigurationsForStore</c>.
    /// </remarks>
    public const string RoutingSortKey = "#routing";

    /// <summary>Builds the PK + SK identifying a store's routing record.</summary>
    /// <param name="storeId">The store.</param>
    public static Dictionary<string, AttributeValue> GetRoutingIdentifiers(long storeId)
        => GetRecordIdentifiers(storeId, RoutingSortKey);

    /// <summary>
    /// Builds the routing write: the routing fields, plus a server-side increment of the version.
    /// </summary>
    /// <remarks>
    /// The version is incremented with <c>ADD</c> rather than written from a value the caller read
    /// earlier, so two concurrent writers cannot land on the same version. Consumers discard updates
    /// carrying a version they have already applied, so a duplicated one would make a real change look
    /// stale and be dropped.
    /// </remarks>
    /// <param name="routing">Supplies the values; the version is assigned here.</param>
    /// <param name="now">The timestamp to stamp onto ModifiedOn, and onto CreatedOn if absent.</param>
    public static PaymentConfigurationUpdate GetRoutingUpdate(StoreRoutingConfiguration routing, DateTimeOffset now)
    {
        if (routing.StoreId <= 0)
            throw new ArgumentException("StoreId is required on the routing configuration.", nameof(routing));

        if (string.IsNullOrWhiteSpace(routing.AcquiringProviderId))
            throw new ArgumentException("AcquiringProviderId is required on the routing configuration.", nameof(routing));

        var update = BuildUpdate(
            now,
            [
                (nameof(StoreRoutingConfiguration.TenantId), routing.TenantId.ToAttributeValue()),
                (nameof(StoreRoutingConfiguration.AcquiringProviderId), routing.AcquiringProviderId.ToAttributeValue()),
                (nameof(StoreRoutingConfiguration.MigrationState),
                    (string.IsNullOrWhiteSpace(routing.MigrationState)
                        ? MigrationStates.PropayActive
                        : routing.MigrationState).ToAttributeValue()),
            ]);

        const string version = nameof(StoreRoutingConfiguration.ConfigurationVersion);
        update.ExpressionAttributeNames[$"#{version}"] = version;
        update.ExpressionAttributeValues[":versionIncrement"] = 1L.ToAttributeValue();

        return update with
        {
            UpdateExpression = $"{update.UpdateExpression} ADD #{version} :versionIncrement",
        };
    }

    /// <summary>Reconstructs a routing record from a DynamoDB item.</summary>
    /// <param name="attributes">The item.</param>
    public static StoreRoutingConfiguration GetRoutingModel(Dictionary<string, AttributeValue> attributes)
        => new()
        {
            StoreId = attributes.GetLong(AttributeNameForPartitionKey) ?? 0,
            TenantId = attributes.GetLong(nameof(StoreRoutingConfiguration.TenantId)),
            AcquiringProviderId = attributes.GetString(nameof(StoreRoutingConfiguration.AcquiringProviderId)) ?? string.Empty,
            MigrationState = attributes.GetString(nameof(StoreRoutingConfiguration.MigrationState)) ?? MigrationStates.PropayActive,
            ConfigurationVersion = attributes.GetLong(nameof(StoreRoutingConfiguration.ConfigurationVersion)) ?? 0,
            CreatedOn = attributes.GetDateTimeOffset(nameof(StoreRoutingConfiguration.CreatedOn)),
            ModifiedOn = attributes.GetDateTimeOffset(nameof(StoreRoutingConfiguration.ModifiedOn)),
        };
}
