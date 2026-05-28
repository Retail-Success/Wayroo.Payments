using Amazon.Lambda.SQSEvents;
using AwesomeAssertions;
using Luci.Orders.SDK.Api;
using Luci.Orders.SDK.Models.StorePropay;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wayroo.Common.Exceptions;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.UnitTests;

public class PaymentConfigurationMessageHandlerTests
{
    private const long AccountNumber = 718040110898;

    private readonly Mock<IStorePropayClient> _storePropayClient = new();
    private readonly Mock<IPaymentConfigurationRepository> _repository = new();

    private PaymentConfigurationMessageHandler CreateHandler() => new(
        _storePropayClient.Object,
        _repository.Object,
        NullLogger<PaymentConfigurationMessageHandler>.Instance);

    private static SQSEvent.SQSMessage MessageWith(string body)
        => new() { MessageId = Guid.NewGuid().ToString(), Body = body };

    /// <summary>
    /// A representative provider webhook. <paramref name="accountNumLiteral"/> is the raw JSON literal
    /// (so callers can supply <c>"\"718...\""</c> for a string, <c>"718..."</c> for a number, etc.).
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
    public async Task Handle_ResolvesStoreFromPayloadAccountNum_AndPersistsBodyVerbatim()
    {
        _storePropayClient
            .Setup(c => c.GetStoreIdFromPropayAccountNumberAsync(AccountNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStoreIdFromPropayAccountNumberResponse { StoreId = 1007, TenantId = 42 });

        PaymentProviderConfiguration? captured = null;
        _repository
            .Setup(r => r.UpsertConfiguration(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentProviderConfiguration configuration, CancellationToken _) =>
            {
                captured = configuration;
                return configuration;
            });

        // The real webhook sends accountNum as a quoted string.
        var body = WebhookBody($"\"{AccountNumber}\"");
        await CreateHandler().Handle(MessageWith(body), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.StoreId.Should().Be(1007);
        captured.TenantId.Should().Be(42);
        captured.ProviderId.Should().Be("propay");
        captured.AccountId.Should().Be(AccountNumber.ToString());
        // The whole body is stored verbatim so no provider field is lost.
        captured.ProviderConfiguration.Should().Be(body);
    }

    [Fact]
    public async Task Handle_AcceptsNumericAccountNum_ForTestPayloads()
    {
        _storePropayClient
            .Setup(c => c.GetStoreIdFromPropayAccountNumberAsync(AccountNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStoreIdFromPropayAccountNumberResponse { StoreId = 1007, TenantId = 42 });

        // accountNum as an unquoted number (what the smoke/integration tests serialize).
        var body = WebhookBody(AccountNumber.ToString());
        await CreateHandler().Handle(MessageWith(body), CancellationToken.None);

        _repository.Verify(
            r => r.UpsertConfiguration(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NoStoreResolved_ThrowsResourceAccess_AndDoesNotUpsert()
    {
        _storePropayClient
            .Setup(c => c.GetStoreIdFromPropayAccountNumberAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<GetStoreIdFromPropayAccountNumberResponse>(null!));

        var act = async () =>
            await CreateHandler().Handle(MessageWith(WebhookBody(AccountNumber.ToString())), CancellationToken.None);

        await act.Should().ThrowAsync<ResourceAccessException>();
        _repository.Verify(
            r => r.UpsertConfiguration(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ResolvedStoreIdNotPositive_ThrowsResourceAccess()
    {
        _storePropayClient
            .Setup(c => c.GetStoreIdFromPropayAccountNumberAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStoreIdFromPropayAccountNumberResponse { StoreId = 0, TenantId = 42 });

        var act = async () =>
            await CreateHandler().Handle(MessageWith(WebhookBody(AccountNumber.ToString())), CancellationToken.None);

        await act.Should().ThrowAsync<ResourceAccessException>();
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
    public async Task Handle_MissingOrInvalidAccountNum_ThrowsFormatException_AndCallsNothing(string body)
    {
        var act = async () => await CreateHandler().Handle(MessageWith(body), CancellationToken.None);

        await act.Should().ThrowAsync<FormatException>();

        _storePropayClient.Verify(
            c => c.GetStoreIdFromPropayAccountNumberAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(
            r => r.UpsertConfiguration(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
