using Amazon.Lambda.SQSEvents;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wayroo.Common.Exceptions;
using Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.UnitTests;

/// <summary>
/// The handler is pure orchestration: parser → store-owner resolver → repository upsert. All
/// provider-specific behaviour (body parsing, Orders SDK lookup) lives behind the two seams and is
/// covered by the provider-specific tests under <c>Gateways/{Provider}/</c>.
/// </summary>
public class PaymentConfigurationMessageHandlerTests
{
    private const string ProviderId = "propay";
    private const long AccountNumber = 718040110898;
    private const long StoreId = 1007;
    private const long TenantId = 42;
    private const string Body = "{\"any\":\"body content the parser knows how to read\"}";
    private const string Configuration = "{\"any\":\"configuration the parser chose to persist\"}";

    private readonly Mock<IPaymentConfigurationParser> _parser = new();
    private readonly Mock<IStoreOwnerResolver> _storeOwnerResolver = new();
    private readonly Mock<IPaymentConfigurationRepository> _repository = new();

    private PaymentConfigurationMessageHandler CreateHandler() => new(
        _parser.Object,
        _storeOwnerResolver.Object,
        _repository.Object,
        NullLogger<PaymentConfigurationMessageHandler>.Instance);

    private static SQSEvent.SQSMessage MessageWith(string body)
        => new() { MessageId = Guid.NewGuid().ToString(), Body = body };

    [Fact]
    public async Task Handle_ParsesBody_ResolvesOwner_AndUpsertsConfigurationFromParsedValues()
    {
        _parser
            .Setup(p => p.Parse(Body))
            .Returns(new ParsedPaymentConfiguration(AccountNumber, ProviderId, Configuration));
        _storeOwnerResolver
            .Setup(r => r.Resolve(AccountNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreOwner(StoreId, TenantId));

        PaymentProviderConfiguration? captured = null;
        _repository
            .Setup(r => r.UpsertConfiguration(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentProviderConfiguration c, CancellationToken _) =>
            {
                captured = c;
                return c;
            });

        await CreateHandler().Handle(MessageWith(Body), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ProviderId.Should().Be(ProviderId);
        captured.StoreId.Should().Be(StoreId);
        captured.TenantId.Should().Be(TenantId);
        captured.AccountId.Should().Be(AccountNumber.ToString());
        captured.ProviderConfiguration.Should().Be(Configuration);
    }

    [Fact]
    public async Task Handle_PropagatesFormatException_FromParser_AndDoesNotResolveOrUpsert()
    {
        _parser
            .Setup(p => p.Parse(It.IsAny<string>()))
            .Throws(new FormatException("malformed body"));

        var act = async () => await CreateHandler().Handle(MessageWith("garbage"), CancellationToken.None);

        await act.Should().ThrowAsync<FormatException>();
        _storeOwnerResolver.Verify(
            r => r.Resolve(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(
            r => r.UpsertConfiguration(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_PropagatesResourceAccessException_FromResolver_AndDoesNotUpsert()
    {
        _parser
            .Setup(p => p.Parse(It.IsAny<string>()))
            .Returns(new ParsedPaymentConfiguration(AccountNumber, ProviderId, Configuration));
        _storeOwnerResolver
            .Setup(r => r.Resolve(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceAccessException("not yet onboarded"));

        var act = async () => await CreateHandler().Handle(MessageWith(Body), CancellationToken.None);

        await act.Should().ThrowAsync<ResourceAccessException>();
        _repository.Verify(
            r => r.UpsertConfiguration(It.IsAny<PaymentProviderConfiguration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
