using AwesomeAssertions;
using Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways.Propay;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.UnitTests.Gateways.Propay;

/// <summary>
/// Provider-specific parser tests: how the ProPay webhook body is read for the routing identity.
/// </summary>
public class PropayConfigurationParserTests
{
    private const long AccountNumber = 718040110898;

    private static PropayConfigurationParser CreateParser() => new();

    /// <summary>
    /// A representative provider webhook. <paramref name="accountNumLiteral"/> is the raw JSON literal
    /// (so callers can supply <c>"\"718…\""</c> for a string, <c>"718…"</c> for a number, etc.).
    /// </summary>
    private static string WebhookBody(string accountNumLiteral) =>
        $$"""
        {
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
        """;

    [Fact]
    public void Parse_ReadsStringAccountNum_FromRealWebhook_AndReturnsRawBodyAsConfiguration()
    {
        // The real webhook always quotes accountNum.
        var body = WebhookBody($"\"{AccountNumber}\"");

        var parsed = CreateParser().Parse(body);

        parsed.AccountNumber.Should().Be(AccountNumber);
        parsed.ProviderId.Should().Be("propay");
        // The whole body is preserved verbatim so no provider field is lost.
        parsed.Configuration.Should().Be(body);
    }

    [Fact]
    public void Parse_ReadsNumericAccountNum_FromTestPayloads()
    {
        var body = WebhookBody(AccountNumber.ToString());

        var parsed = CreateParser().Parse(body);

        parsed.AccountNumber.Should().Be(AccountNumber);
        parsed.ProviderId.Should().Be("propay");
        parsed.Configuration.Should().Be(body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"payload\":{}}")]
    [InlineData("{\"payload\":{\"accountNum\":\"not-a-number\"}}")]
    [InlineData("{\"payload\":{\"accountNum\":\"\"}}")]
    [InlineData("{\"payload\":{\"accountNum\":0}}")]
    [InlineData("{\"payload\":{\"accountNum\":null}}")]
    public void Parse_ThrowsFormatException_ForMissingOrInvalidAccountNum(string body)
    {
        var act = () => CreateParser().Parse(body);

        act.Should().Throw<FormatException>();
    }
}
