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
        configuration.LastModifiedTimestamp = DateTimeOffset.UtcNow;
        configuration.ReceivedTimestamp ??= configuration.LastModifiedTimestamp;

        var request = new PutItemRequest
        {
            TableName = clientOptions.PaymentConfigurationTableName,
            Item = PaymentConfigurationSchemaProvider.GetRecord(configuration),
        };

        logger.LogInformation(
            "Upserting payment configuration for tenant {TenantId} provider {Provider} into {TableName}",
            configuration.TenantId,
            configuration.Provider,
            request.TableName);

        await dynamoDbClient.PutItemAsync(request, cancellationToken);
        return configuration;
    }

    public async Task<PaymentProviderConfiguration?> GetConfiguration(
        string tenantId,
        string provider,
        CancellationToken cancellationToken)
    {
        var request = new GetItemRequest
        {
            TableName = clientOptions.PaymentConfigurationTableName,
            Key = PaymentConfigurationSchemaProvider.GetRecordIdentifiers(tenantId, provider),
        };

        var response = await dynamoDbClient.GetItemAsync(request, cancellationToken);

        return response.Item is { Count: > 0 }
            ? PaymentConfigurationSchemaProvider.GetModel(response.Item)
            : null;
    }
}
