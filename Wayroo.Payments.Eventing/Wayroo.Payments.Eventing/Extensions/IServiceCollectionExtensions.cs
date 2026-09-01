using Amazon.EventBridge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Eventing.Extensions;

public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EventBridge client and the local <see cref="IIntegrationEventPublisher"/> that
    /// publishes to the intraprocess bus (<c>{env}-wayroo-events</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client is registered through a factory so nothing connects to AWS until something actually
    /// publishes — hosts can register this on startup while still being "deployed dark".
    /// </para>
    /// <para>
    /// Reads nothing out of <paramref name="configuration"/> by key: everything is declared on
    /// <see cref="EventBridgePublisherOptions"/> and bound by name. Registration is side-effect free,
    /// so a missing bus ARN now surfaces from startup validation rather than from this method —
    /// see the exception note. That is the same moment in practice, because the recorder lambda builds
    /// the publisher at cold start (its message handler takes one), and it additionally catches a
    /// whitespace-only ARN and a value arriving from a configuration source registered after this call.
    /// </para>
    /// </remarks>
    /// <exception cref="OptionsValidationException">
    /// Thrown from startup validation, not from this method, when <c>WayrooEventsBusArn</c> is missing
    /// or blank. The API's host runs that validation during start; the recorder lambda has no host and
    /// runs it explicitly — see <c>Function.BuildServiceProvider</c>.
    /// </exception>
    public static IServiceCollection AddPaymentsEventPublishing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Bound twice: the bus ARN and the region are root keys (the CDK passes both as env vars),
        // while ServiceUrl hangs off the EventBridge section, mirroring DynamoDb:ServiceUrl in the data
        // access tier. Each Bind appends a configure action and they run in order, so the section
        // overlays the root rather than replacing it.
        services
            .AddOptions<EventBridgePublisherOptions>()
            .Bind(configuration)
            .Bind(configuration.GetSection(EventBridgePublisherOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IAmazonEventBridge>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<EventBridgePublisherOptions>>().Value;

            var eventBridgeConfig = new AmazonEventBridgeConfig
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.AwsRegion),
            };

            // Allows pointing at a local stub, the way the DynamoDb:ServiceUrl override does for the
            // data access layer. EventBridge has no local emulator, so an integration test that drives
            // a real host has no other way to exercise the publish path without reaching AWS.
            //
            // Set after RegionEndpoint and never in the initializer above: the AWS SDK treats the two
            // as mutually exclusive and each clears the other.
            if (!string.IsNullOrEmpty(options.ServiceUrl))
                eventBridgeConfig.ServiceURL = options.ServiceUrl;

            return new AmazonEventBridgeClient(eventBridgeConfig);
        });
        services.TryAddSingleton<IIntegrationEventPublisher, EventBridgeIntegrationEventPublisher>();

        return services;
    }
}
