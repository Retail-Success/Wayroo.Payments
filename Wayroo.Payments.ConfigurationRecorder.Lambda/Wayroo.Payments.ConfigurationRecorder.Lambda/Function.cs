using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways.Propay;
using Wayroo.Payments.DataAccess.Extensions;
using Wayroo.Payments.Eventing.Extensions;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

public class Function
{
    public static readonly string ServiceName = $"{nameof(Wayroo)}{nameof(Payments)}";
    public static readonly string ComponentName = $"{nameof(ConfigurationRecorder)}{nameof(Lambda)}";

    private readonly ILogger<Function> _logger;
    private readonly IMessageHandler _messageHandler;
    private readonly ProcessFailureHandler _failureHandler;

    public Function() : this(new ServiceCollection())
    {
    }

    private Function(IServiceCollection services, ILogger<Function>? logger = null)
    {
        var serviceProvider = BuildServiceProvider(services);
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

    private static IServiceProvider BuildServiceProvider(IServiceCollection services)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console(formatter: new Serilog.Formatting.Json.JsonFormatter())
            .CreateLogger();

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddSystemsManager(options =>
            {
                // Scoped to just the Orders client options the lambda actually uses. The provider
                // strips this whole prefix, so the SSM parameter
                //   /luci/services/utility/OrdersClientOptions/ApiBaseUrl
                // surfaces at config key
                //   ApiBaseUrl
                // (see ParameterStoreKeys). Loaded after env vars so tests can override; Optional = true
                // lets the build succeed when SSM isn't reachable (local component/integration tests).
                options.Path = "/luci/services/utility/OrdersClientOptions/";
                options.ReloadAfter = TimeSpan.FromMinutes(5);
                options.Optional = true;
            })
            .Build();

        ValidateRequiredConfiguration(configuration);

        // Read top-to-bottom as the manifest of what this lambda is wired up with. Each gateway is one
        // line; add a second gateway by sliding in its sibling Add{Provider}Gateway() below.
        //
        // NOTE on multi-gateway: with a single gateway today the handler resolves the one
        // IPaymentConfigurationParser / IStoreOwnerResolver pair straight out of DI. When the upstream
        // webhook envelope starts carrying a provider identifier (the gateway is expected to dispatch
        // with it), introduce a small IPaymentGatewayDispatcher that selects the right pair per
        // message and have PaymentConfigurationMessageHandler ask the dispatcher instead of injecting
        // the pair directly. The Add{Provider}Gateway() pattern below is the same either way.
        services.AddLogging(lb => lb.AddSerilog(Log.Logger));
        services.AddPaymentsDataAccess(configuration);
        // Registers IIntegrationEventPublisher over the intraprocess event bus ({env}-wayroo-events),
        // which is the outbound counterpart to the webhook bus this lambda's queue is fed from.
        // Deliberately inert today — nothing resolves it until D5 has the recorder publish
        // StoreProviderConfigChanged on provider-config write. The EventBridge client is built lazily,
        // so registering it costs a cold start nothing.
        services.AddPaymentsEventPublishing(configuration);
        services.AddPaymentConfigurationRecorder(configuration);
        services.AddPropayGateway(configuration);

        var serviceProvider = services.BuildServiceProvider();

        // The tiers above declare their settings as options classes and validate them with
        // ValidateOnStart(). That only registers an IStartupValidator — for Wayroo.Payments.API the
        // Hosting layer resolves and runs it during host start, but there is no host here, so without
        // this line every one of those validators would be dead code in the process that actually runs
        // the recorder.
        //
        // Before resolving anything, so a misconfigured lambda fails with one OptionsValidationException
        // naming every unusable setting rather than part-way through building the object graph.
        // GetService, not GetRequiredService: a host that registered no validators is fine.
        serviceProvider.GetService<IStartupValidator>()?.Validate();

        return serviceProvider;
    }

    /// <summary>
    /// Fails cold start when a required configuration key is missing entirely.
    /// </summary>
    /// <remarks>
    /// Overlaps with the options validators run above, and both are worth keeping: this answers "is the
    /// key present at all" across every key the lambda needs — including
    /// <see cref="ParameterStoreKeys.OrdersApiBaseUrl"/>, which no options class covers — and names all
    /// the missing ones in one message. The validators answer the narrower "is the bound value usable".
    /// </remarks>
    private static void ValidateRequiredConfiguration(IConfiguration configuration)
    {
        var missing = EnvironmentVariableKeys.Keys()
            .Concat(ParameterStoreKeys.Keys())
            .Where(key => string.IsNullOrWhiteSpace(configuration[key]))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Missing required configuration: {string.Join(", ", missing)}");
    }
}
