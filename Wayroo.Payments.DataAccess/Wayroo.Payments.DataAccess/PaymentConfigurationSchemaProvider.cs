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
    /// Converts a configuration into its full DynamoDB attribute representation.
    /// </summary>
    public static Dictionary<string, AttributeValue> GetRecord(PaymentProviderConfiguration configuration)
    {
        if (configuration.StoreId <= 0)
            throw new ArgumentException("StoreId is required on the configuration.", nameof(configuration));

        if (string.IsNullOrWhiteSpace(configuration.ProviderId))
            throw new ArgumentException("ProviderId is required on the configuration.", nameof(configuration));

        var record = GetRecordIdentifiers(configuration.StoreId, configuration.ProviderId);

        // AccountId is an optional attribute; omit it entirely when absent rather than storing a NULL.
        if (!string.IsNullOrWhiteSpace(configuration.AccountId))
            record[nameof(PaymentProviderConfiguration.AccountId)] = configuration.AccountId.ToAttributeValue();

        record[nameof(PaymentProviderConfiguration.TenantId)] = configuration.TenantId.ToAttributeValue();
        record[nameof(PaymentProviderConfiguration.ProviderConfiguration)] = configuration.ProviderConfiguration.ToAttributeValue();
        record[nameof(PaymentProviderConfiguration.CreatedOn)] = configuration.CreatedOn.ToAttributeValue();
        record[nameof(PaymentProviderConfiguration.ModifiedOn)] = configuration.ModifiedOn.ToAttributeValue();
        return record;
    }

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
            CreatedOn = attributes.GetDateTimeOffset(nameof(PaymentProviderConfiguration.CreatedOn)),
            ModifiedOn = attributes.GetDateTimeOffset(nameof(PaymentProviderConfiguration.ModifiedOn)),
        };
}
