using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

public class Function
{
    public static readonly string ServiceName = $"{nameof(Wayroo)}{nameof(Payments)}";
    public static readonly string ComponentName = $"{nameof(ConfigurationRecorder)}{nameof(Lambda)}";

    private readonly ILogger<Function> _logger;

    public Function() : this(BuildServiceProvider())
    {
    }

    private Function(IServiceProvider serviceProvider, ILogger<Function>? logger = null)
    {
        _logger = logger ?? serviceProvider.GetRequiredService<ILogger<Function>>();
        _logger.LogInformation("{Service} {Component} initialized", ServiceName, ComponentName);
    }

    public Task<SQSBatchResponse> FunctionHandler(SQSEvent input, ILambdaContext context)
    {
        using var _ = _logger.BeginScope(new Dictionary<string, object>
        {
            ["TraceId"] = context.TraceId,
            ["FunctionName"] = context.FunctionName,
            ["AwsRequestId"] = context.AwsRequestId,
            ["InvokedFunctionArn"] = context.InvokedFunctionArn,
        });

        List<SQSBatchResponse.BatchItemFailure> batchItemFailures = [];
        _logger.LogInformation("Received {Count} messages", input.Records.Count);

        // NOTE: Configuration recording is intentionally UNPLUGGED for now — the lambda only logs.
        // The persistence machinery is already built and ready to re-enable:
        //   - Wayroo.Payments.Models (PaymentProviderConfiguration, IPaymentConfigurationRepository)
        //   - Wayroo.Payments.DataAccess (repository, schema provider, AddPaymentsDataAccess)
        //   - the "{env}-PaymentConfiguration" DynamoDB table (provisioned by the CDK stack)
        //   - the PaymentConfigurationMessage DTO and the PaymentConfigurationTableName/AwsRegion env vars
        // To turn recording back on:
        //   1. In BuildServiceProvider: validate EnvironmentVariableKeys.Keys() and call
        //      services.AddPaymentsDataAccess(configuration) (build configuration from env vars first).
        //   2. Resolve IPaymentConfigurationRepository in the constructor.
        //   3. Per record: deserialize PaymentConfigurationMessage from record.Body and call
        //      UpsertConfiguration, adding the MessageId to batchItemFailures on failure.

        _logger.LogInformation("Finished processing messages");
        return Task.FromResult(new SQSBatchResponse(batchItemFailures));
    }

    private static ServiceProvider BuildServiceProvider()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console(formatter: new Serilog.Formatting.Json.JsonFormatter())
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(lb => lb.AddSerilog(Log.Logger));

        return services.BuildServiceProvider();
    }
}
