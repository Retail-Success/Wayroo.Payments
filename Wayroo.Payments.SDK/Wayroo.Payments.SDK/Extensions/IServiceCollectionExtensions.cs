using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wayroo.Payments.DataAccess.Extensions;
using Wayroo.Payments.SDK.Clients;

namespace Wayroo.Payments.SDK.Extensions;

public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Wayroo Payments SDK: the in-process <see cref="IClient"/> plus the underlying DynamoDB
    /// repository wired up by <see cref="Wayroo.Payments.DataAccess.Extensions.IServiceCollectionExtensions.AddPaymentsDataAccess"/>.
    /// </summary>
    /// <remarks>
    /// The signature differs from <c>AddNotificationSDK(string environment)</c> because payments' DataAccess
    /// already takes <see cref="IConfiguration"/> directly (reads <c>AwsRegion</c>, <c>DynamoDb:ServiceUrl</c>,
    /// and <c>PaymentConfigurationTableName</c>). Keep it that way — don't refactor for false symmetry.
    /// </remarks>
    public static IServiceCollection AddPaymentsSDK(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPaymentsDataAccess(configuration);
        services.TryAddSingleton<IClient, Client>();
        return services;
    }
}
