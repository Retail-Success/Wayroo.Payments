using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wayroo.Payments.BusinessLogic.Gateways;
using Wayroo.Payments.BusinessLogic.Gateways.Propay;
using Wayroo.Payments.BusinessLogic.Managers;

namespace Wayroo.Payments.BusinessLogic.Extensions;

public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers the payment account business logic: every provider gateway, the registry that picks
    /// between them, and the manager that resolves which provider a store is on.
    /// </summary>
    /// <remarks>
    /// The single entry point for any host — the API today, a backfill worker or the configuration
    /// recorder later — so that provider selection cannot end up implemented twice.
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">
    /// Supplies the provider credentials and endpoints, and the optional <c>DefaultProviderId</c>.
    /// See <see cref="PaymentGatewayConfigurationKeys"/>.
    /// </param>
    public static IServiceCollection AddPaymentsBusinessLogic(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PaymentGatewayOptions>(options =>
        {
            var configured = configuration[PaymentGatewayConfigurationKeys.DefaultProviderId];
            if (!string.IsNullOrWhiteSpace(configured))
                options.DefaultProviderId = configured;
        });

        services.AddPropayAccountGateway(configuration);

        // Scoped, not singleton: it indexes the gateways, which are themselves scoped. Building it
        // per request costs a dictionary of a handful of entries.
        services.TryAddScoped<IPaymentGatewayRegistry, PaymentGatewayRegistry>();
        services.TryAddScoped<IPaymentAccountManager, PaymentAccountManager>();

        return services;
    }
}
