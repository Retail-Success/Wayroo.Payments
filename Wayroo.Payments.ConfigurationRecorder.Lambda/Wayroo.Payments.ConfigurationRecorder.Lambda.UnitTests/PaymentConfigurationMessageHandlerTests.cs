using System.Text.Json;
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

    private static SQSEvent.SQSMessage MessageFor(
        long? accountNumber,
        string? providerId = "propay",
        string? payload = "{\"merchantKey\":\"secret\"}")
        => new()
        {
            MessageId = Guid.NewGuid().ToString(),
            Body = JsonSerializer.Serialize(new PaymentConfigurationMessage
            {
                AccountNumber = accountNumber,
                ProviderId = providerId,
                ProviderConfiguration = payload,
            }),
        };

    [Fact]
    public async Task Handle_ResolvesStoreAndTenant_ThenUpserts()
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

        await CreateHandler().Handle(MessageFor(AccountNumber), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.StoreId.Should().Be(1007);
        captured.TenantId.Should().Be(42);
        captured.ProviderId.Should().Be("propay");
        captured.AccountId.Should().Be(AccountNumber.ToString());
        captured.ProviderConfiguration.Should().Be("{\"merchantKey\":\"secret\"}");
    }

    [Fact]
    public async Task Handle_NoStoreResolved_ThrowsResourceAccess_AndDoesNotUpsert()
    {
        _storePropayClient
            .Setup(c => c.GetStoreIdFromPropayAccountNumberAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<GetStoreIdFromPropayAccountNumberResponse>(null!));

        var act = async () => await CreateHandler().Handle(MessageFor(AccountNumber), CancellationToken.None);

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

        var act = async () => await CreateHandler().Handle(MessageFor(AccountNumber), CancellationToken.None);

        await act.Should().ThrowAsync<ResourceAccessException>();
    }

    [Theory]
    [InlineData(null, "propay")]
    [InlineData(0L, "propay")]
    [InlineData(AccountNumber, null)]
    [InlineData(AccountNumber, "")]
    public async Task Handle_MissingRequiredFields_ThrowsFormatException_AndCallsNothing(long? accountNumber, string? providerId)
    {
        var act = async () => await CreateHandler().Handle(MessageFor(accountNumber, providerId), CancellationToken.None);

        await act.Should().ThrowAsync<FormatException>();

        _storePropayClient.Verify(
            c => c.GetStoreIdFromPropayAccountNumberAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(
            r => r.UpsertConfiguration(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
