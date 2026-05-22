using System.Reflection;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

public static class EnvironmentVariableKeys
{
    /// <summary>Name of the DynamoDB table the recorder writes configurations to.</summary>
    public static readonly string PaymentConfigurationTableName = nameof(PaymentConfigurationTableName);

    /// <summary>AWS region the DynamoDB client targets.</summary>
    public static readonly string AwsRegion = nameof(AwsRegion);

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
