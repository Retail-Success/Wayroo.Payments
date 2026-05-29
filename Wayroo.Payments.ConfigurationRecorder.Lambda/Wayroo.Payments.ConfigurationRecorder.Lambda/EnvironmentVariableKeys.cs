using System.Reflection;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

public static class EnvironmentVariableKeys
{
    /// <summary>Name of the DynamoDB table the recorder writes configurations to.</summary>
    public static readonly string PaymentConfigurationTableName = nameof(PaymentConfigurationTableName);

    /// <summary>AWS region the DynamoDB and SQS clients target.</summary>
    public static readonly string AwsRegion = nameof(AwsRegion);

    /// <summary>URL of the queue this lambda consumes; messages are re-queued here to retry transient failures.</summary>
    public static readonly string SourceQueueUrl = nameof(SourceQueueUrl);

    /// <summary>URL of the dead-letter queue unrecoverable messages are routed to.</summary>
    public static readonly string DeadLetterQueueUrl = nameof(DeadLetterQueueUrl);

    // Every key declared here is validated on startup by Function.ValidateRequiredEnvironmentVariables.

    public static IEnumerable<string> Keys()
    {
        var fields = typeof(EnvironmentVariableKeys).GetFields(
            BindingFlags.Public | BindingFlags.Static
        );

        return fields
            .Where(field => field.FieldType == typeof(string))
            .Select(field => field.GetValue(null) as string)
            .Where(value => value != null)
            .ToArray()!;
    }
}
