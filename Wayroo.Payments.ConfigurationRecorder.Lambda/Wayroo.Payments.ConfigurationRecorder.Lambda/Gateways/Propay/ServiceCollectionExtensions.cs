using Luci.Orders.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways.Propay;

/// <summary>
/// Registers the ProPay gateway: the Orders SDK client used to call the Orders API plus the
/// provider-specific <see cref="IPaymentConfigurationParser"/> + <see cref="IStoreOwnerResolver"/>
/// implementations. All ProPay wiring lives behind this extension method so the lambda's bootstrap in
/// <see cref="Function"/> stays a short manifest — adding a second gateway is a sibling
/// <c>Add{Provider}Gateway()</c> in <c>Gateways/{Provider}/</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPropayGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOrdersClient(options =>
            options.ApiBaseUrl = configuration[EnvironmentVariableKeys.OrdersApiBaseUrl]!);
        services.AddSingleton<IPaymentConfigurationParser, PropayConfigurationParser>();
        services.AddSingleton<IStoreOwnerResolver, PropayStoreOwnerResolver>();
        return services;
    }
}
