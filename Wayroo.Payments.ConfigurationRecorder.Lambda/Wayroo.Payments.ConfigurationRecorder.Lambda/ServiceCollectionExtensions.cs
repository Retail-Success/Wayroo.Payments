using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

/// <summary>
/// Registers the generic recorder lambda machinery: the SQS client used to re-queue / dead-letter
/// messages, the <see cref="ProcessFailureHandler"/> that decides which queue a failure goes to, and the
/// <see cref="IMessageHandler"/> that orchestrates parser → resolver → upsert. Nothing here is
/// provider-specific — gateway wiring is registered separately via <c>Add{Provider}Gateway()</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentConfigurationRecorder(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(
            Amazon.RegionEndpoint.GetBySystemName(configuration[EnvironmentVariableKeys.AwsRegion] ?? "us-east-1")));
        services.AddSingleton(provider => new ProcessFailureHandler(
            provider.GetRequiredService<ILogger<ProcessFailureHandler>>(),
            provider.GetRequiredService<IAmazonSQS>(),
            configuration[EnvironmentVariableKeys.DeadLetterQueueUrl]!,
            configuration[EnvironmentVariableKeys.SourceQueueUrl]!));
        services.AddSingleton<IMessageHandler, PaymentConfigurationMessageHandler>();
        return services;
    }
}
