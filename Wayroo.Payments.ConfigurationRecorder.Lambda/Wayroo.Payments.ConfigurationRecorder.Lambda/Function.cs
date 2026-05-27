using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Luci.Orders.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Wayroo.Payments.DataAccess.Extensions;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

public class Function
{
    public static readonly string ServiceName = $"{nameof(Wayroo)}{nameof(Payments)}";
    public static readonly string ComponentName = $"{nameof(ConfigurationRecorder)}{nameof(Lambda)}";

    private readonly ILogger<Function> _logger;
    private readonly IMessageHandler _messageHandler;
    private readonly ProcessFailureHandler _failureHandler;

    public Function() : this(BuildServiceProvider())
    {
    }

    private Function(IServiceProvider serviceProvider, ILogger<Function>? logger = null)
    {
        _logger = logger ?? serviceProvider.GetRequiredService<ILogger<Function>>();
        _messageHandler = serviceProvider.GetRequiredService<IMessageHandler>();
        _failureHandler = serviceProvider.GetRequiredService<ProcessFailureHandler>();
        _logger.LogInformation("{Service} {Component} initialized", ServiceName, ComponentName);
    }

    public async Task FunctionHandler(SQSEvent input, ILambdaContext context)
    {
        using var _ = _logger.BeginScope(new Dictionary<string, object>
        {
            ["TraceId"] = context.TraceId,
            ["FunctionName"] = context.FunctionName,
            ["AwsRequestId"] = context.AwsRequestId,
            ["InvokedFunctionArn"] = context.InvokedFunctionArn,
        });

        _logger.LogInformation("Received {Count} messages", input.Records.Count);

        foreach (var message in input.Records)
        {
            using var messageScope = _logger.BeginScope("{MessageId}", message.MessageId);
            try
            {
                using var cts = new CancellationTokenSource(context.RemainingTime);
                await _messageHandler.Handle(message, cts.Token);
            }
            catch (Exception failure)
            {
                try
                {
                    await _failureHandler.HandleFailure(message, failure);
                }
                catch (Exception routingFailure)
                {
                    // Routing itself failed (e.g. SQS unavailable). Rethrow so the whole batch is retried
                    // rather than silently dropping the message.
                    _logger.LogError(routingFailure, "Failed to route the failed message for {MessageId}", message.MessageId);
                    throw;
                }
            }
        }

        _logger.LogInformation("Finished processing messages");
    }

    private static ServiceProvider BuildServiceProvider()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console(formatter: new Serilog.Formatting.Json.JsonFormatter())
            .CreateLogger();

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        ValidateRequiredEnvironmentVariables(configuration);

        var services = new ServiceCollection();
        services.AddLogging(lb => lb.AddSerilog(Log.Logger));

        // DynamoDB persistence.
        services.AddPaymentsDataAccess(configuration);

        // Orders API client — resolves store/tenant from a ProPay account number.
        services.AddOrdersClient(options => options.ApiBaseUrl = configuration[EnvironmentVariableKeys.OrdersApiBaseUrl]!);

        // SQS + failure routing (re-queue transient, dead-letter the rest).
        services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(
            Amazon.RegionEndpoint.GetBySystemName(configuration[EnvironmentVariableKeys.AwsRegion] ?? "us-east-1")));
        services.AddSingleton(provider => new ProcessFailureHandler(
            provider.GetRequiredService<ILogger<ProcessFailureHandler>>(),
            provider.GetRequiredService<IAmazonSQS>(),
            configuration[EnvironmentVariableKeys.DeadLetterQueueUrl]!,
            configuration[EnvironmentVariableKeys.SourceQueueUrl]!));

        services.AddSingleton<IMessageHandler, PaymentConfigurationMessageHandler>();

        return services.BuildServiceProvider();
    }

    private static void ValidateRequiredEnvironmentVariables(IConfiguration configuration)
    {
        var missing = EnvironmentVariableKeys.Keys()
            .Where(key => string.IsNullOrWhiteSpace(configuration[key]))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Missing required environment variables: {string.Join(", ", missing)}");
    }
}
