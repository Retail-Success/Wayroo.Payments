using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.DataAccess;

public class PaymentConfigurationRepository(
    IAmazonDynamoDB dynamoDbClient,
    DynamoDbClientOptions clientOptions,
    ILogger<PaymentConfigurationRepository> logger) : IPaymentConfigurationRepository
{
    public async Task<PaymentProviderConfiguration> UpsertConfiguration(
        PaymentProviderConfiguration configuration,
        CancellationToken cancellationToken)
    {
        configuration.ModifiedOn = DateTimeOffset.UtcNow;
        configuration.CreatedOn ??= configuration.ModifiedOn;

        var request = new PutItemRequest
        {
            TableName = clientOptions.PaymentConfigurationTableName,
            Item = PaymentConfigurationSchemaProvider.GetRecord(configuration),
        };

        logger.LogInformation(
            "Upserting payment configuration for store {StoreId} provider {ProviderId} into {TableName}",
            configuration.StoreId,
            configuration.ProviderId,
            request.TableName);

        await dynamoDbClient.PutItemAsync(request, cancellationToken);
        return configuration;
    }

    public async Task<PaymentProviderConfiguration?> GetConfiguration(
        long storeId,
        string providerId,
        CancellationToken cancellationToken)
    {
        var request = new GetItemRequest
        {
            TableName = clientOptions.PaymentConfigurationTableName,
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
                TableName = clientOptions.PaymentConfigurationTableName,
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
            configurations.AddRange(response.Items.Select(PaymentConfigurationSchemaProvider.GetModel));
            lastEvaluatedKey = response.LastEvaluatedKey is { Count: > 0 } ? response.LastEvaluatedKey : null;
        }
        while (lastEvaluatedKey is not null);

        return configurations;
    }
}
