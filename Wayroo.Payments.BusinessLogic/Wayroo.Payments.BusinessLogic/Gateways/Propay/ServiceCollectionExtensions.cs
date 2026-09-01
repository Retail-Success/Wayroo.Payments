using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RetailSuccess.PaymentGateway.Propay.Configuration;

namespace Wayroo.Payments.BusinessLogic.Gateways.Propay;

/// <summary>
/// Registers the ProPay account gateway: the ProPay/ProtectPay client and the gateway itself.
/// </summary>
/// <remarks>
/// All ProPay wiring lives behind this one extension method, matching <c>AddPropayGateway</c> in the
/// configuration recorder lambda. Adding a second provider is a sibling
/// <c>Add{Provider}AccountGateway()</c> under <c>Gateways/{Provider}/</c>, called from
/// <see cref="Extensions.IServiceCollectionExtensions.AddPaymentsBusinessLogic"/> — the registry
/// picks it up from the registered set with no further wiring.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the ProPay account gateway and the provider client it needs.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">
    /// Supplies the three ProPay/ProtectPay base URIs and the per-tenant credentials. The credentials
    /// arrive from Parameter Store at runtime and are resolved lazily per tenant by the gateway
    /// package, so a tenant with no ProPay credentials only fails when a call is made for it.
    /// </param>
    public static IServiceCollection AddPropayAccountGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPropayClient(
            // The environment-wide default endpoints, from environment variables (the CDK sets them
            // per environment) or appsettings locally. Not from Parameter Store — a tenant's optional
            // per-tenant override comes from there instead, on its credentials, and wins over these.
            configuration.GetSection(PaymentGatewayConfigurationKeys.PropayApiBaseUrisSection).Bind,
            // The credentials are not. AddSystemsManager is already scoped to the vendor path, so
            // /luci/{env}/vendors/propay/tenants/3/termid arrives as the root key tenants:3:termid —
            // under no section at all. Binding a section here (which is what the gateway package's own
            // test fixture does) silently binds nothing, and the failure surfaces only on the first
            // live provider call, because credentials are validated lazily per tenant.
            options => configuration.Bind(options));

        services.AddScoped<IPaymentAccountGateway, PropayAccountGateway>();

        return services;
    }
}
