namespace Wayroo.Payments.DataAccess;

public class DynamoDbClientOptions
{
    /// <summary>
    /// Name of the DynamoDB table storing payment provider configurations.
    /// </summary>
    public string PaymentConfigurationTableName { get; init; } = "PaymentConfiguration";
}
