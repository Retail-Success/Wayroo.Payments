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
    /// <summary>The table's partition key is the TenantId attribute.</summary>
    public static string AttributeNameForPartitionKey => nameof(PaymentProviderConfiguration.TenantId);

    /// <summary>The table's sort key is the Provider attribute.</summary>
    public static string AttributeNameForSortKey => nameof(PaymentProviderConfiguration.Provider);

    /// <summary>
    /// Builds the PK + SK key dictionary used to identify a single configuration record.
    /// </summary>
    public static Dictionary<string, AttributeValue> GetRecordIdentifiers(string tenantId, string provider)
        => new()
        {
            { AttributeNameForPartitionKey, tenantId.ToAttributeValue() },
            { AttributeNameForSortKey, provider.ToAttributeValue() },
        };

    /// <summary>
    /// Converts a configuration into its full DynamoDB attribute representation.
    /// </summary>
    public static Dictionary<string, AttributeValue> GetRecord(PaymentProviderConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.TenantId))
            throw new ArgumentException("TenantId is required on the configuration.", nameof(configuration));

        if (string.IsNullOrWhiteSpace(configuration.Provider))
            throw new ArgumentException("Provider is required on the configuration.", nameof(configuration));

        var record = GetRecordIdentifiers(configuration.TenantId, configuration.Provider);
        record[nameof(PaymentProviderConfiguration.RawPayload)] = configuration.RawPayload.ToAttributeValue();
        record[nameof(PaymentProviderConfiguration.ReceivedTimestamp)] = configuration.ReceivedTimestamp.ToAttributeValue();
        record[nameof(PaymentProviderConfiguration.LastModifiedTimestamp)] = configuration.LastModifiedTimestamp.ToAttributeValue();
        return record;
    }

    /// <summary>
    /// Reconstructs a configuration from a DynamoDB item.
    /// </summary>
    public static PaymentProviderConfiguration GetModel(Dictionary<string, AttributeValue> attributes)
        => new()
        {
            TenantId = attributes.GetString(AttributeNameForPartitionKey) ?? string.Empty,
            Provider = attributes.GetString(AttributeNameForSortKey) ?? string.Empty,
            RawPayload = attributes.GetString(nameof(PaymentProviderConfiguration.RawPayload)),
            ReceivedTimestamp = attributes.GetDateTimeOffset(nameof(PaymentProviderConfiguration.ReceivedTimestamp)),
            LastModifiedTimestamp = attributes.GetDateTimeOffset(nameof(PaymentProviderConfiguration.LastModifiedTimestamp)),
        };
}
