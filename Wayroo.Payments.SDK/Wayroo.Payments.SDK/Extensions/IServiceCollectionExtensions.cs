using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Refit;
using Wayroo.Payments.SDK.Clients;

namespace Wayroo.Payments.SDK.Extensions;

public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Wayroo Payments typed HTTP client: binds <see cref="WayrooPaymentsClientOptions"/>,
    /// wires up Refit's <see cref="IClient"/> against the configured base URL, and surfaces the
    /// <see cref="IHttpClientBuilder"/> for further customization (e.g. X-Ray tracing).
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddWayrooPaymentsClient(
    ///     builder.Configuration.BindSectionByTypeName,
    ///     n => n.AddHttpMessageHandler&lt;HttpClientXRayTracingHandler&gt;());
    /// </code>
    /// </example>
    public static IServiceCollection AddWayrooPaymentsClient(
        this IServiceCollection services,
        Action<WayrooPaymentsClientOptions> configureOptions,
        Action<IHttpClientBuilder>? configureHttpClient = null)
    {
        services.Configure(configureOptions);

        var httpClientBuilder = services
            .AddRefitClient<IClient>()
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<WayrooPaymentsClientOptions>>().Value;

                if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
                {
                    throw new InvalidOperationException(
                        $"{nameof(WayrooPaymentsClientOptions)}.{nameof(WayrooPaymentsClientOptions.ApiBaseUrl)} is not configured.");
                }

                client.BaseAddress = new Uri(options.ApiBaseUrl);
            });

        configureHttpClient?.Invoke(httpClientBuilder);

        return services;
    }
}
