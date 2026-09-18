using System.Text.Json;
using Adyen.BalancePlatform.Extensions;
using Adyen.Core.Auth;
using Adyen.Core.Options;
using Adyen.LegalEntityManagement.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Wayroo.Payments.BusinessLogic.Managers;
using BalancePlatformClient = Adyen.BalancePlatform.Client;
using LegalEntityClient = Adyen.LegalEntityManagement.Client;

namespace Wayroo.Payments.BusinessLogic.Gateways.Adyen;

/// <summary>
/// Registers the Adyen clients this service calls.
/// </summary>
/// <remarks>
/// All Adyen wiring lives behind this one extension method, matching
/// <c>AddPropayAccountGateway</c>.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Adyen account and configuration clients.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Supplies <see cref="AdyenGatewayOptions"/>.</param>
    public static IServiceCollection AddAdyenAccountGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Registered in every host so the endpoints behave the same everywhere; what changes is
        // whether they can do anything. A host with Adyen off refuses as an unsupported provider
        // rather than failing to resolve a dependency mid-request.
        services.TryAddScoped<IAdyenOnboardingManager, AdyenNotEnabledManager>();

        if (!configuration.GetSection(AdyenGatewayConfigurationKeys.Section)
                .GetValue<bool>(nameof(AdyenGatewayOptions.Enabled)))
        {
            return services;
        }

        // From here on Adyen is switched on, so every credential is validated at startup — a missing
        // one fails the deployment instead of a merchant's first request.
        services.RemoveAll<IAdyenOnboardingManager>();
        services.AddScoped<IAdyenOnboardingManager, AdyenOnboardingManager>();

        services
            .AddOptions<AdyenGatewayOptions>()
            .Bind(configuration.GetSection(AdyenGatewayConfigurationKeys.Section))
            // Validated at startup rather than on first use: a missing key and a key with the wrong
            // roles both surface as a 401 on a merchant's first request, long after the deploy that
            // caused it.
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.LegalEntityApiKey),
                $"{AdyenGatewayConfigurationKeys.LegalEntityApiKey} is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.BalancePlatformApiKey),
                $"{AdyenGatewayConfigurationKeys.BalancePlatformApiKey} is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.BalancePlatformId),
                $"{AdyenGatewayConfigurationKeys.BalancePlatformId} is required.")
            .Validate(
                options => !options.UseLiveEndpoints || !string.IsNullOrWhiteSpace(options.LiveEndpointUrlPrefix),
                $"{AdyenGatewayConfigurationKeys.LiveEndpointUrlPrefix} is required when live endpoints are in use.")
            .ValidateOnStart();

        // The two planes authenticate independently. Their token types are distinct closed generics,
        // so registering both leaves no ambiguity for the container to resolve away.
        services.AddSingleton<ITokenProvider<LegalEntityClient.ApiKeyToken>>(provider =>
            new TokenProvider<LegalEntityClient.ApiKeyToken>(
                new LegalEntityClient.ApiKeyToken(
                    Options(provider).LegalEntityApiKey,
                    LegalEntityClient.ClientUtils.ApiKeyHeader.X_API_Key)));

        services.AddSingleton<ITokenProvider<BalancePlatformClient.ApiKeyToken>>(provider =>
            new TokenProvider<BalancePlatformClient.ApiKeyToken>(
                new BalancePlatformClient.ApiKeyToken(
                    Options(provider).BalancePlatformApiKey,
                    BalancePlatformClient.ClientUtils.ApiKeyHeader.X_API_Key)));

        services.AddSingleton(provider => new LegalEntityClient.AdyenOptionsProvider(AdyenOptionsFor(provider)));
        services.AddSingleton(provider => new BalancePlatformClient.AdyenOptionsProvider(AdyenOptionsFor(provider)));
        // The generated models carry their own converters, so the provider only needs somewhere to
        // hang them; the library supplies no default instance.
        services.AddSingleton(_ => new LegalEntityClient.JsonSerializerOptionsProvider(new JsonSerializerOptions()));
        services.AddSingleton(_ => new BalancePlatformClient.JsonSerializerOptionsProvider(new JsonSerializerOptions()));

        // Only the rungs the onboarding ladder walks. The library registers one typed client per
        // service, so taking the whole API surface would mean managing HTTP clients we never call.
        services.AddLegalEntitiesService();
        services.AddBusinessLinesService();
        services.AddHostedOnboardingService();
        services.AddAccountHoldersService();
        services.AddBalanceAccountsService();

        services.AddScoped<IAdyenOnboardingGateway, AdyenOnboardingGateway>();

        return services;
    }

    private static AdyenGatewayOptions Options(IServiceProvider provider)
        => provider.GetRequiredService<IOptions<AdyenGatewayOptions>>().Value;

    private static AdyenOptions AdyenOptionsFor(IServiceProvider provider)
    {
        var options = Options(provider);

        return new AdyenOptions
        {
            Environment = options.UseLiveEndpoints ? AdyenEnvironment.Live : AdyenEnvironment.Test,
            LiveEndpointUrlPrefix = options.LiveEndpointUrlPrefix,
        };
    }
}
