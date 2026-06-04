using System.Reflection;

namespace Wayroo.Payments.API;

/// <summary>
/// Required configuration keys validated at startup. The API exits if any are missing.
/// </summary>
public static class EnvironmentVariableKeys
{
    public static readonly string AspNetCoreEnvironment = nameof(AspNetCoreEnvironment);
    public static readonly string Environment = nameof(Environment);
    public static readonly string AwsRegion = nameof(AwsRegion);
    public static readonly string OtelServiceName = "OpenTelemetry:ServiceName";
    public static readonly string OtelServiceVersion = "OpenTelemetry:ServiceVersion";
    public static readonly string OtelExporterOtlpEndpoint = "OpenTelemetry:ExporterOtlpEndpoint";
    public static readonly string OtelExporterOtlpProtocol = "OpenTelemetry:ExporterOtlpProtocol";
    public static readonly string OtelTracesSampler = "OpenTelemetry:TracesSampler";
    public static readonly string OtelPropagators = "OpenTelemetry:Propagators";

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
