using AwesomeAssertions;
using Moq;
using Wayroo.Payments.BusinessLogic.Gateways;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.UnitTests.Gateways;

public class PaymentGatewayRegistryTests
{
    private static IPaymentAccountGateway GatewayFor(string providerId)
    {
        var gateway = new Mock<IPaymentAccountGateway>();
        gateway.SetupGet(g => g.ProviderId).Returns(providerId);
        return gateway.Object;
    }

    [Fact]
    public void TryGet_FindsARegisteredGateway()
    {
        var registry = new PaymentGatewayRegistry([GatewayFor("propay"), GatewayFor("adyen")]);

        registry.TryGet("adyen", out var gateway).Should().BeTrue();
        gateway.ProviderId.Should().Be("adyen");
    }

    [Theory]
    [InlineData("PROPAY")]
    [InlineData("ProPay")]
    [InlineData("  propay  ")]
    public void TryGet_MatchesCaseInsensitivelyAndIgnoresSurroundingWhitespace(string providerId)
    {
        var registry = new PaymentGatewayRegistry([GatewayFor("propay")]);

        registry.TryGet(providerId, out var gateway).Should().BeTrue();
        gateway.ProviderId.Should().Be("propay");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("stripe")]
    public void TryGet_ReturnsFalse_ForAProviderWithNoGateway(string? providerId)
    {
        var registry = new PaymentGatewayRegistry([GatewayFor("propay")]);

        registry.TryGet(providerId, out _).Should().BeFalse();
    }

    [Fact]
    public void KnownProviderIds_ListsEveryRegisteredProvider()
    {
        var registry = new PaymentGatewayRegistry([GatewayFor("propay"), GatewayFor("adyen")]);

        registry.KnownProviderIds.Should().BeEquivalentTo("propay", "adyen");
    }

    [Fact]
    public void KnownProviderIds_IsEmpty_WhenNothingIsRegistered()
    {
        new PaymentGatewayRegistry([]).KnownProviderIds.Should().BeEmpty();
    }

    /// <summary>
    /// Two gateways claiming one provider would otherwise resolve by registration order, and the two
    /// would be reaching different providers — a silent wrong answer about a merchant's money.
    /// </summary>
    [Fact]
    public void Constructor_Throws_WhenTwoGatewaysClaimTheSameProvider()
    {
        var act = () => new PaymentGatewayRegistry([GatewayFor("propay"), GatewayFor("PROPAY")]);

        act.Should().Throw<ArgumentException>().WithMessage("*propay*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenAGatewayDeclaresNoProvider(string providerId)
    {
        var act = () => new PaymentGatewayRegistry([GatewayFor(providerId)]);

        act.Should().Throw<ArgumentException>();
    }
}
