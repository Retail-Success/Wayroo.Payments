using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wayroo.Payments.BusinessLogic.Extensions;

namespace Wayroo.Payments.BusinessLogic.UnitTests;

/// <summary>
/// Pins how <see cref="PaymentGatewayOptions.DefaultProviderId"/> is bound from configuration.
/// </summary>
/// <remarks>
/// <para>
/// The interesting case is a key that is <b>present and empty</b>, not one that is absent. Binding
/// skips absent keys, so the default survives on its own there — but an ECS task definition or lambda
/// environment entry set to <c>""</c> arrives as a present, empty value, and writing that through
/// would leave <see cref="Managers.PaymentAccountManager"/> resolving a gateway named <c>""</c> and
/// raising <c>PaymentProviderAmbiguousException</c> for every store with no routing record — which is
/// most of them.
/// </para>
/// <para>
/// This is the guard on the reason the whole provider-routing mechanism is inert for existing stores,
/// and it is cheap to break: the invariant lives in a property setter that looks redundant next to a
/// field initializer.
/// </para>
/// </remarks>
public class PaymentGatewayOptionsBindingTests
{
    private static PaymentGatewayOptions Bind(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair =>
                new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

        var services = new ServiceCollection();
        // AddPaymentsBusinessLogic also wires the ProPay gateway, which needs logging.
        services.AddLogging();
        services.AddPaymentsBusinessLogic(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<PaymentGatewayOptions>>().Value;
    }

    [Fact]
    public void DefaultProviderId_FallsBackToThePlatformDefault_WhenNothingIsConfigured()
        => Bind().DefaultProviderId.Should().Be(PaymentGatewayOptions.PlatformDefaultProviderId);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DefaultProviderId_KeepsThePlatformDefault_WhenTheKeyIsPresentButBlank(string configured)
        => Bind((PaymentGatewayConfigurationKeys.DefaultProviderId, configured))
            .DefaultProviderId.Should().Be(PaymentGatewayOptions.PlatformDefaultProviderId);

    [Fact]
    public void DefaultProviderId_TakesAConfiguredValue()
        => Bind((PaymentGatewayConfigurationKeys.DefaultProviderId, "adyen"))
            .DefaultProviderId.Should().Be("adyen");

    [Fact]
    public void DefaultProviderId_HoldsTheInvariant_WhenSetDirectly()
    {
        // Not a duplicate of the binding cases: the rule lives on the property precisely so it also
        // covers a host or a test that never goes through configuration.
        var options = new PaymentGatewayOptions { DefaultProviderId = "" };

        options.DefaultProviderId.Should().Be(PaymentGatewayOptions.PlatformDefaultProviderId);
    }
}
