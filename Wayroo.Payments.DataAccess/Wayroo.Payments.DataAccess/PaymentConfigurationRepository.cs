using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wayroo.Payments.DataAccess.Extensions;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.DataAccess;

/// <summary>
/// DynamoDB-backed <see cref="IPaymentConfigurationRepository"/>.
/// </summary>
/// <remarks>
/// Every writer uses <c>UpdateItem</c> rather than <c>PutItem</c>. Independent callers share a record —
/// the SQS recorder writes the provider's credential payload and the account refresh writes the
/// provider's account information — and a whole-item put by either would silently erase the other's
/// attributes. Each writer names only what it owns.
/// <para>
/// The store's routing lives in the same partition under its own reserved sort key, so writing it
/// cannot disturb either of those, and reading a store's provider configurations has to skip it.
/// </para>
/// </remarks>
public class PaymentConfigurationRepository(
    IAmazonDynamoDB dynamoDbClient,
    IOptions<DynamoDbClientOptions> clientOptions,
    ILogger<PaymentConfigurationRepository> logger) : IPaymentConfigurationRepository
{
    // Captured once. IOptions<T> is a fixed snapshot anyway, and this repository is a singleton with
    // no reload semantics, so re-reading .Value on every request would only add noise.
    private readonly string _tableName = clientOptions.Value.PaymentConfigurationTableName;

    public async Task<PaymentProviderConfiguration> UpsertConfiguration(
        PaymentProviderConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var update = PaymentConfigurationSchemaProvider.GetConfigurationUpdate(configuration, now);

        logger.LogInformation(
            "Upserting payment configuration for store {StoreId} provider {ProviderId} into {TableName}",
            configuration.StoreId,
            configuration.ProviderId,
            _tableName);

        return await ApplyUpdate(configuration, update, now, cancellationToken);
    }

    public async Task<PaymentProviderConfiguration> UpsertAccountDetails(
        PaymentProviderConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var update = PaymentConfigurationSchemaProvider.GetAccountDetailsUpdate(configuration, now);

        // Never log ProviderAccountDetails — it is the provider's payload and carries account PII.
        logger.LogInformation(
            "Recording {ProviderId} account details for store {StoreId} (status {AccountStatus}) into {TableName}",
            configuration.ProviderId,
            configuration.StoreId,
            configuration.AccountStatus,
            _tableName);

        return await ApplyUpdate(configuration, update, now, cancellationToken);
    }

    public async Task<StoreRoutingConfiguration?> GetRouting(long storeId, CancellationToken cancellationToken)
    {
        var request = new GetItemRequest
        {
            TableName = _tableName,
            Key = PaymentConfigurationSchemaProvider.GetRoutingIdentifiers(storeId),
        };

        var response = await dynamoDbClient.GetItemAsync(request, cancellationToken);

        return response.Item is { Count: > 0 }
            ? PaymentConfigurationSchemaProvider.GetRoutingModel(response.Item)
            : null;
    }

    public async Task<StoreRoutingWriteResult> UpsertRouting(
        StoreRoutingConfiguration routing,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var update = PaymentConfigurationSchemaProvider.GetRoutingUpdate(routing, now);

        var request = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = PaymentConfigurationSchemaProvider.GetRoutingIdentifiers(routing.StoreId),
            UpdateExpression = update.UpdateExpression,
            ExpressionAttributeNames = update.ExpressionAttributeNames,
            ExpressionAttributeValues = update.ExpressionAttributeValues,
            // ALL_OLD rather than ALL_NEW: the previous values are what say whether the routing
            // actually moved, and the new version is simply the old one plus the increment we just
            // applied. Asking for the new values instead would leave no way to tell a real change from
            // a rewrite without a second, racy read.
            ReturnValues = ReturnValue.ALL_OLD,
        };

        logger.LogInformation(
            "Routing store {StoreId} to {AcquiringProviderId} (migration state {MigrationState}) in {TableName}",
            routing.StoreId,
            routing.AcquiringProviderId,
            routing.MigrationState,
            _tableName);

        var response = await dynamoDbClient.UpdateItemAsync(request, cancellationToken);

        var previous = response.Attributes is { Count: > 0 }
            ? PaymentConfigurationSchemaProvider.GetRoutingModel(response.Attributes)
            : null;

        var current = new StoreRoutingConfiguration
        {
            StoreId = routing.StoreId,
            TenantId = routing.TenantId,
            AcquiringProviderId = routing.AcquiringProviderId,
            MigrationState = string.IsNullOrWhiteSpace(routing.MigrationState)
                ? MigrationStates.PropayActive
                : routing.MigrationState,
            ConfigurationVersion = (previous?.ConfigurationVersion ?? 0) + 1,
            CreatedOn = previous?.CreatedOn ?? now,
            ModifiedOn = now,
        };

        return new StoreRoutingWriteResult(current, previous);
    }

    public async Task<PaymentProviderConfiguration?> GetConfiguration(
        long storeId,
        string providerId,
        CancellationToken cancellationToken)
    {
        var request = new GetItemRequest
        {
            TableName = _tableName,
            Key = PaymentConfigurationSchemaProvider.GetRecordIdentifiers(storeId, providerId),
        };

        var response = await dynamoDbClient.GetItemAsync(request, cancellationToken);

        return response.Item is { Count: > 0 }
            ? PaymentConfigurationSchemaProvider.GetModel(response.Item)
            : null;
    }

    public async Task<IReadOnlyList<PaymentProviderConfiguration>> GetConfigurationsForStore(
        long storeId,
        CancellationToken cancellationToken)
    {
        var configurations = new List<PaymentProviderConfiguration>();
        Dictionary<string, AttributeValue>? lastEvaluatedKey = null;

        do
        {
            var request = new QueryRequest
            {
                TableName = _tableName,
                KeyConditionExpression = "#storeId = :storeId",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#storeId"] = PaymentConfigurationSchemaProvider.AttributeNameForPartitionKey,
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":storeId"] = PaymentConfigurationSchemaProvider.GetPartitionKeyValue(storeId),
                },
                ExclusiveStartKey = lastEvaluatedKey,
            };

            var response = await dynamoDbClient.QueryAsync(request, cancellationToken);

            // The partition also holds the store's routing record under a reserved sort key. It is not
            // a provider configuration and must not be handed back as one.
            configurations.AddRange(response.Items
                .Where(item => !string.Equals(
                    item.GetString(PaymentConfigurationSchemaProvider.AttributeNameForSortKey),
                    PaymentConfigurationSchemaProvider.RoutingSortKey,
                    StringComparison.Ordinal))
                .Select(PaymentConfigurationSchemaProvider.GetModel));
            lastEvaluatedKey = response.LastEvaluatedKey is { Count: > 0 } ? response.LastEvaluatedKey : null;
        }
        while (lastEvaluatedKey is not null);

        return configurations;
    }

    /// <summary>
    /// Runs the update and returns the merged record as DynamoDB now holds it, so a caller sees the
    /// other writer's attributes too rather than only the ones it supplied.
    /// </summary>
    private async Task<PaymentProviderConfiguration> ApplyUpdate(
        PaymentProviderConfiguration configuration,
        PaymentConfigurationUpdate update,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var request = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = PaymentConfigurationSchemaProvider.GetRecordIdentifiers(
                configuration.StoreId,
                configuration.ProviderId),
            UpdateExpression = update.UpdateExpression,
            ExpressionAttributeNames = update.ExpressionAttributeNames,
            ExpressionAttributeValues = update.ExpressionAttributeValues,
            ReturnValues = ReturnValue.ALL_NEW,
        };

        var response = await dynamoDbClient.UpdateItemAsync(request, cancellationToken);

        if (response.Attributes is { Count: > 0 })
            return PaymentConfigurationSchemaProvider.GetModel(response.Attributes);

        // A store that returns nothing is unexpected, but the write itself succeeded — reflect the
        // caller's own values back rather than failing the call.
        configuration.ModifiedOn = now;
        configuration.CreatedOn ??= now;
        return configuration;
    }
}
