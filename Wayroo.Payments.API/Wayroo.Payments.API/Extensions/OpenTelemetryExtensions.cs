using OpenTelemetry;
using OpenTelemetry.Contrib.Extensions.AWSXRay.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Sampler.AWS;
using OpenTelemetry.Trace;

namespace Wayroo.Payments.API.Extensions;

/// <summary>
/// Configures OpenTelemetry tracing with AWS X-Ray sampling and OTLP export.
/// Mirrors the equivalent extension in <c>Wayroo.Notification.API</c>.
/// </summary>
public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddOpenTelemetryWithXRay(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var serviceName = configuration[EnvironmentVariableKeys.OtelServiceName] ?? "";
        var serviceVersion = configuration[EnvironmentVariableKeys.OtelServiceVersion] ?? "";
        var otlpEndpoint = configuration[EnvironmentVariableKeys.OtelExporterOtlpEndpoint] ?? "";

        var resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(serviceName: serviceName)
            .AddAWSEC2Detector();

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddAWSEC2Detector()
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: serviceVersion))
            .WithTracing(tracing => tracing
                .AddSource(serviceName)
                .AddAspNetCoreInstrumentation()
                .AddOtlpExporter()
                .SetSampler(AWSXRayRemoteSampler.Builder(resourceBuilder.Build())
                    .SetEndpoint(otlpEndpoint)
                    .Build()));

        Sdk.SetDefaultTextMapPropagator(new AWSXRayPropagator());

        return services;
    }
}
