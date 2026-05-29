using System.Text.Json;
using Amazon;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Wayroo.Payments.Infrastructure.SmokeTests;

/// <summary>
/// Smoke test that exercises the deployed Payments configuration pipeline through its only entry point
/// (the SQS configuration queue): drop a message on the queue, then verify via CloudWatch Logs that the
/// recorder lambda received and processed it without errors.
/// </summary>
/// <remarks>
/// Mirrors <c>Wayroo.Notification.Recorder.Lambda.IntegrationTests.LambdaFunctionTests.CanReceiveEventsAndLog</c>
/// — verification is log-based rather than data-based so the test only needs <c>logs:FilterLogEvents</c>
/// (not table scan/get/delete permissions) and doesn't leave residue in the deployed DynamoDB table.
/// <para>
/// Each environment must publish a known-good <c>TestPropayAccountNumber</c> that resolves to a real
/// store via the Orders API, otherwise the lambda will dead-letter and the negative error-log check
/// will fail.
/// </para>
/// <para>
/// NOTE: this file is currently ProPay-only by accident of being the only gateway live today — the
/// message body it sends mirrors the MerchantWare/ProPay webhook shape. When a second gateway is added
/// (see <c>Gateways/{Provider}/</c> in the recorder lambda for where the per-provider classes live),
/// split this file into per-provider smoke tests (e.g. <c>Propay/ConfigurationRecorderSmokeTests.cs</c>
/// alongside <c>Stripe/…</c>) so the "smoke test = ProPay" assumption doesn't get baked into the project
/// structure permanently.
/// </para>
/// </remarks>
public class ConfigurationRecorderSmokeTests
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(60);

    private readonly ITestOutputHelper _output;
    private readonly string _queueName;
    private readonly string _logGroupName;
    private readonly long _testAccountNumber;
    private readonly IAmazonSQS _sqsClient;
    private readonly IAmazonCloudWatchLogs _cloudWatchLogsClient;

    public ConfigurationRecorderSmokeTests(ITestOutputHelper output)
    {
        _output = output;

        var environment = Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "dev";
        _output.WriteLine($"Running smoke tests against environment: {environment}");

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile($"appsettings.{environment}.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        _queueName = config["ConfigurationQueueName"]
            ?? throw new System.InvalidOperationException("ConfigurationQueueName is not configured.");
        _logGroupName = config["RecorderLambdaLogGroupName"]
            ?? throw new System.InvalidOperationException("RecorderLambdaLogGroupName is not configured.");

        var rawAccount = config["TestPropayAccountNumber"];
        if (string.IsNullOrWhiteSpace(rawAccount) || !long.TryParse(rawAccount, out _testAccountNumber) || _testAccountNumber <= 0)
        {
            throw new System.InvalidOperationException(
                $"TestPropayAccountNumber must be configured in appsettings.{environment}.json with a known-good ProPay account number that resolves to a real store in the {environment} environment.");
        }

        _sqsClient = new AmazonSQSClient(RegionEndpoint.USEast1);
        _cloudWatchLogsClient = new AmazonCloudWatchLogsClient(RegionEndpoint.USEast1);
    }

    [Fact]
    public async Task SendingMessageToConfigurationQueue_ProducesSuccessfulRecorderLogEntry()
    {
        // Hard-coded to "propay" — matches the recorder's parser default until upstream EventBridge
        // dispatch routes a provider through. The body shape mirrors the real MerchantWare/ProPay
        // webhook the lambda processes in production.
        const string smokeTestProviderId = "propay";

        var messageBody = JsonSerializer.Serialize(new
        {
            notificationId = Guid.NewGuid().ToString(),
            eventType = "merchantware.credentials.created",
            eventDateTimeUTC = DateTime.UtcNow.ToString("MM/dd/yyyy HH:mm:ss"),
            payload = new
            {
                accountNum = _testAccountNumber,
                merchantId = "smoke-test",
                merchantName = "smoke-test",
                tapToPay = new
                {
                    terminalId = "smoke-test",
                    activationCode = "smoke-test",
                    merchantSiteId = "smoke-test",
                    merchantKey = "smoke-test",
                },
            },
        });

        var queueUrl = (await _sqsClient.GetQueueUrlAsync(_queueName)).QueueUrl;
        _output.WriteLine($"Sending smoke test message to {queueUrl} with ProviderId={smokeTestProviderId}");

        // Capture the search window's lower bound BEFORE sending — anything the lambda logs for our
        // specific MessageId must land within [preInvokeTime, now] and the filter uses that to scope
        // out unrelated noise from the shared log group.
        var preInvokeTime = DateTimeOffset.UtcNow;
        var sendResponse = await _sqsClient.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = messageBody,
        });
        var sqsMessageId = sendResponse.MessageId;
        _output.WriteLine($"Sent SQS message {sqsMessageId}; polling {_logGroupName} for a log entry tagged with this MessageId...");

        // Positive check: at least one structured log entry from the recorder where
        // Properties.MessageId equals our SQS MessageId. The recorder enriches its logs with the
        // SQS MessageId on the per-message scope (see Function.cs / PaymentConfigurationMessageHandler).
        var foundProcessingLog = await PollUntilLogMatchesAsync(
            filterPattern: $"{{$.Properties.MessageId = \"{sqsMessageId}\"}}",
            startTime: preInvokeTime);

        foundProcessingLog.Should().BeTrue(
            $"expected the recorder lambda to log a structured entry tagged with MessageId={sqsMessageId} within {PollTimeout.TotalSeconds}s; if this fails, the lambda likely never received the message (check the source queue → lambda event source mapping and the lambda's VPC/networking).");

        // Negative check: in the same time window, scoped to this MessageId, no Error-level entries.
        // If the lambda dead-letters (e.g. Orders unreachable, account not found), the recorder logs
        // an Error template ("Unrecoverable error processing message. Dead-lettering.") which would
        // match here and flag the test red.
        var errorRequest = new FilterLogEventsRequest
        {
            FilterPattern = $"{{$.Level = \"Error\" && $.Properties.MessageId = \"{sqsMessageId}\"}}",
            StartTime = preInvokeTime.ToUnixTimeMilliseconds(),
            EndTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            LogGroupName = _logGroupName,
            Limit = 5,
        };

        var errorEvents = (await _cloudWatchLogsClient.FilterLogEventsAsync(errorRequest)).Events ?? [];
        if (errorEvents.Count > 0)
        {
            _output.WriteLine($"Error log events for MessageId={sqsMessageId}: {string.Join(" | ", errorEvents.Select(e => e.Message))}");
        }

        errorEvents.Should().BeEmpty(
            $"the recorder lambda should not log any Error-level entries while processing MessageId={sqsMessageId}.");
    }

    private async Task<bool> PollUntilLogMatchesAsync(string filterPattern, DateTimeOffset startTime)
    {
        var deadline = DateTime.UtcNow.Add(PollTimeout);

        while (DateTime.UtcNow < deadline)
        {
            var request = new FilterLogEventsRequest
            {
                FilterPattern = filterPattern,
                StartTime = startTime.ToUnixTimeMilliseconds(),
                EndTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LogGroupName = _logGroupName,
                Limit = 5,
            };

            var response = await _cloudWatchLogsClient.FilterLogEventsAsync(request);
            if (response.Events is { Count: > 0 })
            {
                return true;
            }

            _output.WriteLine($"No matching log entry yet, retrying in {PollInterval.TotalSeconds}s...");
            await Task.Delay(PollInterval);
        }

        return false;
    }
}
