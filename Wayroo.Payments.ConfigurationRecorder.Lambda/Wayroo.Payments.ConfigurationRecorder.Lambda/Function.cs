using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Wayroo.Payments.DataAccess.Extensions;
using Wayroo.Payments.Models;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

public class Function
{
    public static readonly string ServiceName = $"{nameof(Wayroo)}{nameof(Payments)}";
    public static readonly string ComponentName = $"{nameof(ConfigurationRecorder)}{nameof(Lambda)}";

    private static readonly JsonSerializerOptions MessageSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<Function> _logger;
    private readonly IPaymentConfigurationRepository _repository;

    public Function() : this(BuildServiceProvider())
    {
    }

    private Function(IServiceProvider serviceProvider, ILogger<Function>? logger = null)
    {
        _logger = logger ?? serviceProvider.GetRequiredService<ILogger<Function>>();
        _repository = serviceProvider.GetRequiredService<IPaymentConfigurationRepository>();
        _logger.LogInformation("{Service} {Component} initialized", ServiceName, ComponentName);
    }

    public async Task<SQSBatchResponse> FunctionHandler(SQSEvent input, ILambdaContext context)
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

        foreach (var record in input.Records)
        {
            try
            {
                var configuration = MapToConfiguration(record);
                if (configuration is null)
                {
                    // Routing fields are missing; the message can never succeed, so don't retry it.
                    // (Returning it as a batch failure would just loop until the DLQ.) Logged inside MapToConfiguration.
                    batchItemFailures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = record.MessageId });
                    continue;
                }

                await _repository.UpsertConfiguration(configuration, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Never log record.Body — it carries payment credentials.
                _logger.LogError(ex, "Failed to record configuration for message {MessageId}", record.MessageId);
                batchItemFailures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = record.MessageId });
            }
        }

        _logger.LogInformation("Finished processing messages");
        return new SQSBatchResponse(batchItemFailures);
    }

    private PaymentProviderConfiguration? MapToConfiguration(SQSEvent.SQSMessage record)
    {
        var message = JsonSerializer.Deserialize<PaymentConfigurationMessage>(record.Body, MessageSerializerOptions);

        if (message is null
            || message.StoreId is null or <= 0
            || string.IsNullOrWhiteSpace(message.ProviderId))
        {
            _logger.LogError(
                "Message {MessageId} is missing required routing fields (StoreId/ProviderId)",
                record.MessageId);
            return null;
        }

        return new PaymentProviderConfiguration
        {
            StoreId = message.StoreId.Value,
            ProviderId = message.ProviderId,
            TenantId = message.TenantId,
            AccountId = message.AccountId,
            ProviderConfiguration = message.ProviderConfiguration,
        };
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
        services.AddPaymentsDataAccess(configuration);

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
