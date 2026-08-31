using Amazon.DynamoDBv2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.DataAccess.Extensions;

public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers the DynamoDB client and the payment configuration repository.
    /// </summary>
    /// <remarks>
    /// Reads nothing out of <paramref name="configuration"/> by key — everything this library needs is
    /// declared on <see cref="DynamoDbClientOptions"/> and bound by name. A missing required value is
    /// reported by startup validation, not discovered on the first query.
    /// </remarks>
    public static IServiceCollection AddPaymentsDataAccess(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.ConfigureDynamoDb(configuration);
        services.TryAddSingleton<IPaymentConfigurationRepository, PaymentConfigurationRepository>();
        return services;
    }

    private static void ConfigureDynamoDb(this IServiceCollection services, IConfiguration configuration)
    {
        // Bound twice, because the settings genuinely live at two different depths: the table name and
        // the region are root keys (that is how the CDK passes them as container/lambda env vars),
        // while ServiceUrl hangs off the DynamoDb section so it reads the same way as the eventing
        // tier's EventBridge:ServiceUrl. Each Bind appends a configure action and they run in order,
        // so the section overlays the root rather than replacing it.
        services
            .AddOptions<DynamoDbClientOptions>()
            .Bind(configuration)
            .Bind(configuration.GetSection(DynamoDbClientOptions.SectionName))
            .ValidateDataAnnotations()
            // Runs during host start for Wayroo.Payments.API. The recorder lambda has no host, so it
            // calls IStartupValidator itself — see Function.BuildServiceProvider.
            .ValidateOnStart();

        services.TryAddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<DynamoDbClientOptions>>().Value;

            var dynamoConfig = new AmazonDynamoDBConfig
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.AwsRegion),
            };

            // Two statements, ServiceURL last, deliberately: the AWS SDK treats RegionEndpoint and
            // ServiceURL as mutually exclusive and setting either clears the other. Folding these into
            // the initializer above would drop the local endpoint and send every DynamoDB Local
            // integration test at real AWS, where it fails looking like a credentials problem.
            if (!string.IsNullOrEmpty(options.ServiceUrl))
                dynamoConfig.ServiceURL = options.ServiceUrl;

            return dynamoConfig;
        });

        services.TryAddSingleton<IAmazonDynamoDB>(provider =>
            new AmazonDynamoDBClient(provider.GetRequiredService<AmazonDynamoDBConfig>()));
    }
}
