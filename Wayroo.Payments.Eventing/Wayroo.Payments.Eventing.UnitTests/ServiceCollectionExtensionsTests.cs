using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wayroo.Common.Models.Events;
using Wayroo.Payments.Eventing.Extensions;

namespace Wayroo.Payments.Eventing.UnitTests;

public class ServiceCollectionExtensionsTests
{
    private const string DevBusArn = "arn:aws:events:us-east-1:203538442868:event-bus/dev-wayroo-events";

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair =>
                new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    [Fact]
    public void AddPaymentsEventPublishing_RegistersThePublisher()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPaymentsEventPublishing(Configuration(
            (EventingConfigurationKeys.WayrooEventsBusArn, DevBusArn),
            (EventingConfigurationKeys.AwsRegion, "us-east-1")));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IIntegrationEventPublisher>()
            .Should().BeOfType<EventBridgeIntegrationEventPublisher>();
        provider.GetRequiredService<EventBridgePublisherOptions>()
            .EventBusArn.Should().Be(DevBusArn);
    }

    [Fact]
    public void AddPaymentsEventPublishing_MissingBusArn_ThrowsOnRegistration()
    {
        var services = new ServiceCollection();

        // Fail at cold start rather than on the first publish — a host missing the bus ARN would
        // otherwise look healthy right up until it silently published to the default bus.
        var act = () => services.AddPaymentsEventPublishing(Configuration());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{EventingConfigurationKeys.WayrooEventsBusArn}*");
    }
}
