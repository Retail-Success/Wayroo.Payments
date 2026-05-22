using Amazon.DynamoDBv2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.DataAccess.Extensions;

public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers the DynamoDB client and the payment configuration repository.
    /// </summary>
    public static IServiceCollection AddPaymentsDataAccess(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.ConfigureDynamoDb(configuration);
        services.TryAddSingleton<IPaymentConfigurationRepository, PaymentConfigurationRepository>();
        return services;
    }

    private static void ConfigureDynamoDb(this IServiceCollection services, IConfiguration configuration)
    {
        var dynamoSection = configuration.GetSection("DynamoDb");

        var dynamoConfig = new AmazonDynamoDBConfig
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(configuration["AwsRegion"] ?? "us-east-1")
        };

        // Allows pointing at DynamoDB Local for integration tests.
        var serviceUrl = dynamoSection["ServiceUrl"];
        if (!string.IsNullOrEmpty(serviceUrl))
            dynamoConfig.ServiceURL = serviceUrl;

        services.TryAddSingleton(dynamoConfig);
        services.TryAddSingleton<IAmazonDynamoDB>(provider =>
            new AmazonDynamoDBClient(provider.GetRequiredService<AmazonDynamoDBConfig>()));

        services.TryAddSingleton(new DynamoDbClientOptions
        {
            PaymentConfigurationTableName =
                configuration["PaymentConfigurationTableName"] ?? "PaymentConfiguration"
        });
    }
}
