using System.Text.Json;
using AwesomeAssertions;
using Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways.Propay;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.UnitTests.Gateways.Propay;

/// <summary>
/// Provider-specific parser tests: how the EventBridge-wrapped ProPay webhook event is read for the
/// routing identity, and how only the inner <c>detail</c> block is preserved as configuration.
/// </summary>
public class PropayConfigurationParserTests
{
    private const long AccountNumber = 718040110898;

    private static PropayConfigurationParser CreateParser() => new();

    /// <summary>
    /// A representative EventBridge envelope wrapping a ProPay webhook body. <paramref
    /// name="accountNumLiteral"/> is the raw JSON literal (so callers can supply
    /// <c>"\"718…\""</c> for a string, <c>"718…"</c> for a number, etc.).
    /// </summary>
    private static string EventBridgeEnvelope(string accountNumLiteral) =>
        $$"""
        {
            "version": "0",
            "id": "d561edd4-9952-27f9-c74c-8401625d7bac",
            "detail-type": "merchantware.credentials.created",
            "source": "propay.webhook",
            "account": "203538442868",
            "time": "2026-06-01T18:37:58Z",
            "region": "us-east-1",
            "resources": [],
            "detail": {
                "notificationId": "3c8d75eb-9a90-4cee-be17-d937362033cb",
                "eventType": "merchantware.credentials.created",
                "eventDateTimeUTC": "04/17/2025 10:37:42",
                "payload": {
                    "accountNum": {{accountNumLiteral}},
                    "merchantId": "290031234BK1765",
                    "merchantName": "Shoppers Stop1",
                    "tapToPay": {
                        "terminalId": "76539084",
                        "activationCode": "9874923947293%9899700098XHKL",
                        "merchantSiteId": "3Q28PMZA",
                        "merchantKey": "4ZV7Q-DMJ6R-AZEGY-NNYFE-CNFF3"
                    }
                }
            }
        }
        """;

    private static string DetailBlockOf(string envelope)
    {
        using var document = JsonDocument.Parse(envelope);
        return document.RootElement.GetProperty("detail").GetRawText();
    }

    [Fact]
    public void Parse_ReadsStringAccountNum_FromRealEnvelope_AndReturnsDetailAsConfiguration()
    {
        // The real webhook always quotes accountNum.
        var envelope = EventBridgeEnvelope($"\"{AccountNumber}\"");

        var parsed = CreateParser().Parse(envelope);

        parsed.AccountNumber.Should().Be(AccountNumber);
        parsed.ProviderId.Should().Be("propay");
        // Only the inner 'detail' block is preserved — the EventBridge envelope is dropped.
        parsed.Configuration.Should().Be(DetailBlockOf(envelope));
    }

    [Fact]
    public void Parse_ReadsNumericAccountNum_FromTestPayloads()
    {
        var envelope = EventBridgeEnvelope(AccountNumber.ToString());

        var parsed = CreateParser().Parse(envelope);

        parsed.AccountNumber.Should().Be(AccountNumber);
        parsed.ProviderId.Should().Be("propay");
        parsed.Configuration.Should().Be(DetailBlockOf(envelope));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"detail\":{}}")]
    [InlineData("{\"detail\":{\"payload\":{}}}")]
    [InlineData("{\"detail\":{\"payload\":{\"accountNum\":\"not-a-number\"}}}")]
    [InlineData("{\"detail\":{\"payload\":{\"accountNum\":\"\"}}}")]
    [InlineData("{\"detail\":{\"payload\":{\"accountNum\":0}}}")]
    [InlineData("{\"detail\":{\"payload\":{\"accountNum\":null}}}")]
    // Bare body without the EventBridge envelope must be rejected — the recorder now only accepts
    // events delivered through the bus.
    [InlineData("{\"payload\":{\"accountNum\":\"718040110898\"}}")]
    public void Parse_ThrowsFormatException_ForMissingOrInvalidAccountNum(string body)
    {
        var act = () => CreateParser().Parse(body);

        act.Should().Throw<FormatException>();
    }
}
