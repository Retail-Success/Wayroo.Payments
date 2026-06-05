using System.Text.Json;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.TestUtilities;
using AwesomeAssertions;
using Wayroo.Payments.ConfigurationRecorder.Lambda.IntegrationTests.Fixtures;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.IntegrationTests;

/// <summary>
/// Integration tests for the recorder lambda. Exercise the real <see cref="Function"/> against a real
/// (local) DynamoDB instance and a WireMock HTTP stub of the Orders API.
/// </summary>
[Collection(nameof(TestCollection))]
public class LambdaFunctionTests(TestFixture fixture)
{
    [Fact]
    public void CanCreateFunction()
    {
        // Given configuration values for the environment have been set. Iterate both env-var keys AND
        // SSM-backed parameter store keys (Function validates both); the env-var provider surfaces the
        // SSM keys at the same config key, so the SSM source — which is Optional = true — can be skipped
        // when SSM isn't reachable (as it isn't in build-time test runs).
        foreach (var key in EnvironmentVariableKeys.Keys().Concat(ParameterStoreKeys.Keys()))
        {
            var mockValue = $"some-{key}-value";

            if (key.EndsWith("Url"))
            {
                mockValue = $"https://some-{key}-value.local";
            }

            Environment.SetEnvironmentVariable(key, mockValue);
        }

        // When
        Function? lambda = null;

        var initializationException = Record.Exception(() => lambda = new Function());

        // Then
        initializationException.Should().BeNull();
        lambda.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessesSqsEvent_AndWritesResolvedConfigurationToDynamoDB()
    {
        // Given a local DynamoDB + Orders stub, with the recorder configured to point at both
        var dynamoDbClient = await fixture.StartDynamoDbLocalAsync();
        var tableName = await fixture.EstablishTableAsync(dynamoDbClient);
        var ordersStub = fixture.GetOrCreateOrdersStub();

        const long accountNumber = 203538442868;
        const long expectedStoreId = 1007;
        const long expectedTenantId = 42;
        const string expectedProviderId = "propay";

        // Stub the Orders endpoint the lookup will hit. Serve both PascalCase and camelCase so the
        // Refit deserializer matches regardless of its case-sensitivity configuration.
        ordersStub
            .Given(Request.Create()
                .WithPath($"/propay/accounts/{accountNumber}/store")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(
                    $"{{\"StoreId\":{expectedStoreId},\"TenantId\":{expectedTenantId}," +
                    $"\"storeId\":{expectedStoreId},\"tenantId\":{expectedTenantId}}}"));

        SetRecorderEnvironment(tableName);

        // Mirrors the EventBridge envelope the recorder's queue actually receives (the
        // {env}-webhook-bus rule wraps the provider webhook in this envelope before SQS delivery).
        // The recorder reads detail.payload.accountNum and persists the inner 'detail' object as
        // ProviderConfiguration; the surrounding envelope (version/id/source/region/…) is dropped.
        var detail = new
        {
            notificationId = Guid.NewGuid().ToString(),
            eventType = "merchantware.credentials.created",
            eventDateTimeUTC = "04/17/2025 10:37:42",
            // ProPay sends the payload as a JSON string, so serialize it here to mirror the real shape.
            payload = JsonSerializer.Serialize(new
            {
                accountNum = accountNumber.ToString(),
                merchantId = "290031234BK1765",
                merchantName = "Shoppers Stop1",
                tapToPay = new
                {
                    terminalId = "76539084",
                    activationCode = "9874923947293%9899700098XHKL",
                    merchantSiteId = "3Q28PMZA",
                    merchantKey = "4ZV7Q-DMJ6R-AZEGY-NNYFE-CNFF3",
                },
            }),
        };
        var messageBody = JsonSerializer.Serialize(new
        {
            version = "0",
            id = Guid.NewGuid().ToString(),
            detailType = "merchantware.credentials.created",
            source = "propay.webhook",
            account = "203538442868",
            time = DateTime.UtcNow.ToString("O"),
            region = "us-east-1",
            resources = Array.Empty<string>(),
            detail,
        });
        
        var function = new Function();
        var sqsEvent = new SQSEvent
        {
            Records = new List<SQSEvent.SQSMessage>
            {
                new() { MessageId = Guid.NewGuid().ToString(), Body = messageBody },
            },
        };

        // When the lambda processes the SQS event
        await function.FunctionHandler(sqsEvent, new TestLambdaContext { RemainingTime = TimeSpan.FromMinutes(5) });

        // Then a configuration row exists in DynamoDB with the resolved store/tenant + body verbatim
        var response = await dynamoDbClient.GetItemAsync(new GetItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["StoreId"] = new() { N = expectedStoreId.ToString() },
                ["ProviderId"] = new() { S = expectedProviderId },
            },
        });

        response.Item.Should().NotBeNull();
        response.Item.Should().ContainKey("StoreId");
        response.Item["StoreId"].N.Should().Be(expectedStoreId.ToString());
        response.Item["ProviderId"].S.Should().Be(expectedProviderId);
        response.Item["TenantId"].N.Should().Be(expectedTenantId.ToString());
        response.Item["AccountId"].S.Should().Be(accountNumber.ToString());
        response.Item["ProviderConfiguration"].S.Should().BeEquivalentTo(detail.payload);
        response.Item.Should().ContainKey("CreatedOn");
        response.Item.Should().ContainKey("ModifiedOn");
    }

    private void SetRecorderEnvironment(string tableName)
    {
        Environment.SetEnvironmentVariable(EnvironmentVariableKeys.PaymentConfigurationTableName, tableName);
        Environment.SetEnvironmentVariable(EnvironmentVariableKeys.AwsRegion, "us-east-1");
        // The Orders URL is normally loaded from SSM; the env-var provider surfaces it at the same
        // config key (the SSM source is Optional, so it's skipped when SSM isn't reachable).
        Environment.SetEnvironmentVariable(ParameterStoreKeys.OrdersApiBaseUrl, fixture.OrdersApiBaseUrl);
        Environment.SetEnvironmentVariable(EnvironmentVariableKeys.SourceQueueUrl, "https://sqs.local/source");
        Environment.SetEnvironmentVariable(EnvironmentVariableKeys.DeadLetterQueueUrl, "https://sqs.local/dlq");

        // Point the data-access AmazonDynamoDBClient at DynamoDB Local (consumed via the "DynamoDb"
        // configuration section in Wayroo.Payments.DataAccess).
        Environment.SetEnvironmentVariable("DynamoDb__ServiceUrl", fixture.DynamoServiceUrl);

        // The SDK's default credential chain expects creds even for DynamoDB Local.
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "notUsed");
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "notUsed");
    }
}
