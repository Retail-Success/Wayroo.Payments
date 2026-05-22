using Wayroo.Payments.Infrastructure.Resources;

namespace Wayroo.Payments.Infrastructure;

internal class InfrastructureResources
{
    // TEMP: re-enable alongside the DynamoDB table in ResourceStack.
    // public required PaymentConfigurationTable PaymentConfigurationTable { get; init; }
    public required ConfigurationRecorderLambda ConfigurationRecorderLambda { get; init; }
}
