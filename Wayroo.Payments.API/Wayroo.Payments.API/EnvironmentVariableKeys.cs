using System.Reflection;
using Wayroo.Payments.BusinessLogic;

namespace Wayroo.Payments.API;

/// <summary>
/// Required configuration keys validated at startup. The API exits if any are missing.
/// </summary>
public static class EnvironmentVariableKeys
{
    public static readonly string AspNetCoreEnvironment = nameof(AspNetCoreEnvironment);
    public static readonly string Environment = nameof(Environment);
    public static readonly string AwsRegion = nameof(AwsRegion);
    public static readonly string PaymentConfigurationTableName = nameof(PaymentConfigurationTableName);

    // Sourced from the tier that reads it so the two can't drift, matching how the recorder lambda
    // takes WayrooEventsBusArn from Wayroo.Payments.Eventing.
    public static readonly string PropaySecretsPath = PaymentGatewayConfigurationKeys.PropaySecretsPath;

    // The ProPay/ProtectPay endpoints. All three are listed so a missing one fails at startup: the
    // gateway package builds a typed HttpClient per API and only throws on a blank base address when
    // that client is first constructed, which would otherwise surface as a 500 on a merchant's first
    // balance request instead of a failed deployment.
    //
    // The per-tenant ProPay *credentials* are deliberately not listed. They arrive from Parameter
    // Store and are resolved lazily per tenant, so requiring them here would block a deployment over
    // a tenant nobody is calling for. Nor is DefaultProviderId, which has a working default.
    public static readonly string PropayRestBaseUrl = PaymentGatewayConfigurationKeys.PropayRestBaseUrl;
    public static readonly string PropayXmlBaseUrl = PaymentGatewayConfigurationKeys.PropayXmlBaseUrl;
    public static readonly string ProtectPayRestBaseUrl = PaymentGatewayConfigurationKeys.ProtectPayRestBaseUrl;
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
