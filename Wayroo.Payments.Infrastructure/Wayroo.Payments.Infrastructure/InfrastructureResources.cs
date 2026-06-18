using Wayroo.Payments.Infrastructure.Resources;

namespace Wayroo.Payments.Infrastructure;

internal class InfrastructureResources
{
    public required PaymentConfigurationTable PaymentConfigurationTable { get; init; }
    public required ConfigurationRecorderLambda ConfigurationRecorderLambda { get; init; }
    public required PaymentsAPI PaymentsAPI { get; init; }
}
