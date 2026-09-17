using Adyen.BalancePlatform.Services;
using Adyen.LegalEntityManagement.Services;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wayroo.Payments.BusinessLogic.Gateways.Adyen;

namespace Wayroo.Payments.BusinessLogic.UnitTests.Gateways.Adyen;

/// <summary>
/// How the Adyen clients are wired, and which version of each API they speak.
/// </summary>
/// <remarks>
/// <para>
/// <b>The version assertions are the point of this class.</b> Adyen versions its APIs in the URL
/// path, and the client library picks the version for us: a package upgrade can move an endpoint to a
/// new major version, where request shapes and required fields differ, without a single line of this
/// service changing. These tests turn that into a build failure rather than something discovered on a
/// seller's account.
/// </para>
/// <para>
/// The credential validation is checked here too, because it runs at startup rather than on first
/// use: a missing key and a key carrying the wrong roles both surface as a 401 on some merchant's
/// first request, long after the deploy that caused it.
/// </para>
/// </remarks>
public class AdyenGatewayWiringTests
{
    private const string LegalEntityApiVersion = "/lem/v4";
    private const string BalancePlatformApiVersion = "/bcl/v2";

    private static ServiceProvider Provider(params (string Key, string? Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            [AdyenGatewayConfigurationKeys.LegalEntityApiKey] = "lem-key",
            [AdyenGatewayConfigurationKeys.BalancePlatformApiKey] = "bcl-key",
            [AdyenGatewayConfigurationKeys.BalancePlatformId] = "RetailSuccess",
        };

        foreach (var (key, value) in overrides)
            settings[key] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdyenAccountGateway(configuration);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Legal Entity Management v4. The earlier v3 differs in the shapes this service sends, and the
    /// onboarding design was settled against v4.
    /// </summary>
    [Fact]
    public void TheLegalEntityClients_SpeakVersion4()
    {
        using var provider = Provider();

        BaseAddressOf<ILegalEntitiesService, LegalEntitiesService>(provider)
            .Should()
            .Contain(LegalEntityApiVersion);

        BaseAddressOf<IBusinessLinesService, BusinessLinesService>(provider)
            .Should()
            .Contain(LegalEntityApiVersion);

        BaseAddressOf<IHostedOnboardingService, HostedOnboardingService>(provider)
            .Should()
            .Contain(LegalEntityApiVersion);
    }

    /// <summary>
    /// The balance platform Configuration API v2 — where account holders and balance accounts live,
    /// and where money-bearing objects are created.
    /// </summary>
    [Fact]
    public void TheBalancePlatformClients_SpeakVersion2()
    {
        using var provider = Provider();

        BaseAddressOf<IAccountHoldersService, AccountHoldersService>(provider)
            .Should()
            .Contain(BalancePlatformApiVersion);

        BaseAddressOf<IBalanceAccountsService, BalanceAccountsService>(provider)
            .Should()
            .Contain(BalancePlatformApiVersion);
    }

    /// <summary>
    /// The two planes are separate hosts as well as separate credentials, so a key or an endpoint
    /// crossed over between them fails rather than quietly acting on the wrong platform.
    /// </summary>
    [Fact]
    public void TheTwoPlanes_AreDifferentHosts()
    {
        using var provider = Provider();

        BaseAddressOf<ILegalEntitiesService, LegalEntitiesService>(provider)
            .Should()
            .NotBe(BaseAddressOf<IAccountHoldersService, AccountHoldersService>(provider));
    }

    /// <summary>
    /// Nothing in this service should be able to reach Adyen's live platform unless it was configured
    /// to: dev and QA both run against the test one.
    /// </summary>
    [Fact]
    public void TheClients_CallTheTestPlatformUnlessLiveIsConfigured()
    {
        using var provider = Provider();

        BaseAddressOf<ILegalEntitiesService, LegalEntitiesService>(provider).Should().Contain("-test.");
        BaseAddressOf<IAccountHoldersService, AccountHoldersService>(provider).Should().Contain("-test.");
    }

    [Fact]
    public void TheOnboardingGateway_IsResolvable()
    {
        using var provider = Provider();

        provider.GetRequiredService<IAdyenOnboardingGateway>().Should().BeOfType<AdyenOnboardingGateway>();
    }

    [Theory]
    [InlineData(nameof(AdyenGatewayOptions.LegalEntityApiKey))]
    [InlineData(nameof(AdyenGatewayOptions.BalancePlatformApiKey))]
    [InlineData(nameof(AdyenGatewayOptions.BalancePlatformId))]
    public void ABlankCredential_FailsAtStartupRatherThanOnAMerchantsFirstRequest(string setting)
    {
        using var provider = Provider(($"{AdyenGatewayConfigurationKeys.Section}:{setting}", string.Empty));

        var read = () => provider.GetRequiredService<IOptions<AdyenGatewayOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage($"*{setting}*");
    }

    /// <summary>
    /// Adyen's live endpoints are prefixed per customer and there is no usable default, so live with
    /// no prefix would send every request somewhere that does not exist.
    /// </summary>
    [Fact]
    public void LiveEndpoints_RequireTheCustomerPrefix()
    {
        using var provider = Provider((AdyenGatewayConfigurationKeys.UseLiveEndpoints, "true"));

        var read = () => provider.GetRequiredService<IOptions<AdyenGatewayOptions>>().Value;

        read.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{nameof(AdyenGatewayOptions.LiveEndpointUrlPrefix)}*");
    }

    /// <summary>
    /// Which Adyen environment we call and which of ours we stamp on a reference are separate
    /// settings: dev and QA are different environments of ours that both run against Adyen's test
    /// platform, and conflating them would have QA writing references that read as dev's.
    /// </summary>
    [Fact]
    public void TheReferenceEnvironment_IsSeparateFromTheAdyenEnvironment()
    {
        using var provider = Provider(
            (AdyenGatewayConfigurationKeys.ReferenceEnvironment, nameof(Models.AdyenReferenceEnvironment.Qa)));

        var options = provider.GetRequiredService<IOptions<AdyenGatewayOptions>>().Value;

        options.ReferenceEnvironment.Should().Be(Models.AdyenReferenceEnvironment.Qa);
        options.UseLiveEndpoints.Should().BeFalse();
    }

    /// <summary>
    /// The base address the library gave a client, which is where the API version it speaks is
    /// written down.
    /// </summary>
    private static string BaseAddressOf<TService, TImplementation>(IServiceProvider provider)
        where TService : notnull
        where TImplementation : class, TService
    {
        var service = provider.GetRequiredService<TService>() as TImplementation
            ?? throw new InvalidOperationException(
                $"{typeof(TService).Name} did not resolve to {typeof(TImplementation).Name}.");

        // Reached through the property rather than a shared interface because the generated clients
        // expose no common one; if a library upgrade hides it, this fails loudly rather than
        // silently stopping checking the version.
        var httpClient = (HttpClient?)typeof(TImplementation)
            .GetProperty(nameof(HttpClient))
            ?.GetValue(service);

        return httpClient?.BaseAddress?.ToString()
               ?? throw new InvalidOperationException(
                   $"{typeof(TImplementation).Name} has no readable base address, so the API version "
                   + "it speaks can no longer be pinned this way.");
    }
}
