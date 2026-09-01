using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        provider.GetRequiredService<IOptions<EventBridgePublisherOptions>>()
            .Value.EventBusArn.Should().Be(DevBusArn);
    }

    [Fact]
    public void AddPaymentsEventPublishing_BindsTheServiceUrlOverrideFromItsSection()
    {
        // The bus ARN and region are root keys while ServiceUrl sits under a section, so the options
        // are bound in two passes. This pins that the second pass overlays the first rather than
        // replacing it — get the order wrong and the ARN comes back blank.
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPaymentsEventPublishing(Configuration(
            (EventingConfigurationKeys.WayrooEventsBusArn, DevBusArn),
            ($"{EventingConfigurationKeys.EventBridgeSection}:ServiceUrl", "http://localhost:4566")));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<EventBridgePublisherOptions>>().Value;

        options.EventBusArn.Should().Be(DevBusArn);
        options.ServiceUrl.Should().Be("http://localhost:4566");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddPaymentsEventPublishing_MissingBusArn_FailsStartupValidation(string? busArn)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Registration itself is side-effect free now. The gate is ValidateOnStart, which the API's
        // host runs during start and which the recorder lambda runs explicitly (it has no host) —
        // see Function.BuildServiceProvider. Either way a misconfigured host fails before it can look
        // healthy and silently publish to the account's default bus.
        services.AddPaymentsEventPublishing(busArn is null
            ? Configuration()
            : Configuration((EventingConfigurationKeys.WayrooEventsBusArn, busArn)));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{EventingConfigurationKeys.WayrooEventsBusArn}*");
    }

    [Fact]
    public void AddPaymentsEventPublishing_MissingBusArn_AlsoRefusesToBuildThePublisher()
    {
        // Belt and braces on the resolve path: even a host that never runs startup validation cannot
        // end up with a publisher pointed at the default bus.
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPaymentsEventPublishing(Configuration());
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IIntegrationEventPublisher>();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{EventingConfigurationKeys.WayrooEventsBusArn}*");
    }
}
