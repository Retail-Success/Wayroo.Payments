using Amazon.EventBridge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Eventing.Extensions;

public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EventBridge client and the local <see cref="IIntegrationEventPublisher"/> that
    /// publishes to the intraprocess bus (<c>{env}-wayroo-events</c>).
    /// </summary>
    /// <remarks>
    /// The client is registered through a factory so nothing connects to AWS until something actually
    /// publishes — hosts can register this on startup while still being "deployed dark".
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <c>WayrooEventsBusArn</c> is missing from configuration. Failing here (cold start) rather than
    /// on the first publish keeps a misconfigured host from looking healthy.
    /// </exception>
    public static IServiceCollection AddPaymentsEventPublishing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var eventBusArn = configuration[EventingConfigurationKeys.WayrooEventsBusArn];
        if (string.IsNullOrWhiteSpace(eventBusArn))
        {
            throw new InvalidOperationException(
                $"Missing required configuration: {EventingConfigurationKeys.WayrooEventsBusArn}. " +
                "The CDK passes it through from the WayrooEventsBusArn stack parameter.");
        }

        services.TryAddSingleton(new EventBridgePublisherOptions { EventBusArn = eventBusArn });
        services.TryAddSingleton<IAmazonEventBridge>(_ =>
        {
            var eventBridgeConfig = new AmazonEventBridgeConfig
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(
                    configuration[EventingConfigurationKeys.AwsRegion] ?? "us-east-1"),
            };

            // Allows pointing at a local stub, the way the DynamoDb:ServiceUrl override does for the
            // data access layer. EventBridge has no local emulator, so an integration test that drives
            // a real host has no other way to exercise the publish path without reaching AWS.
            var serviceUrl = configuration.GetSection(EventingConfigurationKeys.EventBridgeSection)["ServiceUrl"];
            if (!string.IsNullOrEmpty(serviceUrl))
                eventBridgeConfig.ServiceURL = serviceUrl;

            return new AmazonEventBridgeClient(eventBridgeConfig);
        });
        services.TryAddSingleton<IIntegrationEventPublisher, EventBridgeIntegrationEventPublisher>();

        return services;
    }
}
