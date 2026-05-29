using System.Text.Json;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Wayroo.Payments.Infrastructure.SmokeTests;

/// <summary>
/// Smoke test that exercises the deployed Payments configuration pipeline through its only entry point
/// (the SQS configuration queue): drop a message on the queue, wait for the recorder lambda to resolve
/// the store/tenant and write the row, then clean up.
/// </summary>
/// <remarks>
/// Mirrors <c>Wayroo.ContentLibrary.Infrastructure.SmokeTests</c>. Picks <c>appsettings.{env}.json</c>
/// from the <c>ENVIRONMENT</c> environment variable (defaults to <c>dev</c>); each environment must
/// publish a known-good <c>TestPropayAccountNumber</c> that resolves to a real store via the Orders API.
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
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(60);

    private readonly ITestOutputHelper _output;
    private readonly string _queueName;
    private readonly string _tableName;
    private readonly long _testAccountNumber;
    private readonly IAmazonSQS _sqsClient;
    private readonly IAmazonDynamoDB _dynamoDbClient;

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
            ?? throw new InvalidOperationException("ConfigurationQueueName is not configured.");
        _tableName = config["ConfigurationTableName"]
            ?? throw new InvalidOperationException("ConfigurationTableName is not configured.");

        var rawAccount = config["TestPropayAccountNumber"];
        if (string.IsNullOrWhiteSpace(rawAccount) || !long.TryParse(rawAccount, out _testAccountNumber) || _testAccountNumber <= 0)
        {
            throw new InvalidOperationException(
                $"TestPropayAccountNumber must be configured in appsettings.{environment}.json with a known-good ProPay account number that resolves to a real store in the {environment} environment.");
        }

        _sqsClient = new AmazonSQSClient(RegionEndpoint.USEast1);
        _dynamoDbClient = new AmazonDynamoDBClient(RegionEndpoint.USEast1);
    }

    [Fact]
    public async Task SendingMessageToConfigurationQueue_PersistsResolvedConfiguration()
    {
        // Hard-coded to "propay" to match the handler's default until the upstream EventBridge
        // integration dispatches the provider. NOTE: this means the smoke test overwrites (and then
        // deletes) the real (storeId, "propay") row for the configured test account — make sure
        // TestPropayAccountNumber always points at a designated test account whose propay config you
        // are OK with the smoke test clobbering.
        const string smokeTestProviderId = "propay";
        var smokeTestMarker = DateTime.UtcNow.ToString("O");

        // Mirrors the real MerchantWare/ProPay webhook shape so the body we send matches what the lambda
        // will see in production. Only payload.accountNum drives routing; the whole body is persisted
        // verbatim as ProviderConfiguration. The marker on merchantName is what we use to recognise this
        // smoke-test run when we go to verify + clean up.
        var messageBody = JsonSerializer.Serialize(new
        {
            notificationId = Guid.NewGuid().ToString(),
            eventType = "merchantware.credentials.created",
            eventDateTimeUTC = DateTime.UtcNow.ToString("MM/dd/yyyy HH:mm:ss"),
            payload = new
            {
                accountNum = _testAccountNumber,
                merchantId = "smoke-test",
                merchantName = $"smoke-test-{smokeTestMarker}",
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

        var sendResponse = await _sqsClient.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = messageBody,
        });
        _output.WriteLine($"Sent SQS message {sendResponse.MessageId}; polling {_tableName} for the resulting row...");

        Dictionary<string, AttributeValue>? persisted = null;
        try
        {
            persisted = await PollForPersistedRowAsync(smokeTestProviderId);

            persisted.Should().NotBeNull(
                $"expected a configuration row with ProviderId '{smokeTestProviderId}' to appear in {_tableName} within {PollTimeout.TotalSeconds}s.");
            persisted!["AccountId"].S.Should().Be(_testAccountNumber.ToString());
            persisted["ProviderConfiguration"].S.Should().Be(messageBody);
            persisted["TenantId"].N.Should().NotBeNullOrEmpty();
            _output.WriteLine($"Row found: StoreId={persisted["StoreId"].N}, TenantId={persisted["TenantId"].N}");
        }
        finally
        {
            if (persisted is not null)
            {
                await _dynamoDbClient.DeleteItemAsync(new DeleteItemRequest
                {
                    TableName = _tableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["StoreId"] = persisted["StoreId"],
                        ["ProviderId"] = persisted["ProviderId"],
                    },
                });
                _output.WriteLine($"Cleaned up smoke-test row (StoreId={persisted["StoreId"].N}, ProviderId={smokeTestProviderId})");
            }
        }
    }

    private async Task<Dictionary<string, AttributeValue>?> PollForPersistedRowAsync(string providerId)
    {
        var deadline = DateTime.UtcNow.Add(PollTimeout);

        while (DateTime.UtcNow < deadline)
        {
            // ProviderId isn't indexed, so scan with a filter. The table is small and the test runs
            // infrequently, so the read cost is acceptable.
            var scan = await _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "#providerId = :providerId",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#providerId"] = "ProviderId",
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":providerId"] = new() { S = providerId },
                },
                Limit = 1,
            });

            if (scan.Items is { Count: > 0 })
            {
                return scan.Items[0];
            }

            _output.WriteLine($"Row not present yet, retrying in {PollInterval.TotalSeconds}s...");
            await Task.Delay(PollInterval);
        }

        return null;
    }
}
