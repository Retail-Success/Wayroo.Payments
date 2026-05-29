using AwesomeAssertions;
using Luci.Orders.SDK.Api;
using Luci.Orders.SDK.Models.StorePropay;
using Moq;
using Wayroo.Common.Exceptions;
using Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways;
using Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways.Propay;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.UnitTests.Gateways.Propay;

/// <summary>
/// Provider-specific store-owner resolver tests: how the ProPay account number is looked up via the
/// Orders API and what counts as a transient (re-queue) failure.
/// </summary>
public class PropayStoreOwnerResolverTests
{
    private const long AccountNumber = 718040110898;

    private readonly Mock<IStorePropayClient> _storePropayClient = new();

    private PropayStoreOwnerResolver CreateResolver() => new(_storePropayClient.Object);

    [Fact]
    public async Task Resolve_ReturnsStoreOwner_WhenOrdersResolvesStore()
    {
        _storePropayClient
            .Setup(c => c.GetStoreIdFromPropayAccountNumberAsync(AccountNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStoreIdFromPropayAccountNumberResponse { StoreId = 1007, TenantId = 42 });

        var owner = await CreateResolver().Resolve(AccountNumber, CancellationToken.None);

        owner.Should().Be(new StoreOwner(1007, 42));
    }

    [Fact]
    public async Task Resolve_ThrowsResourceAccess_WhenOrdersReturnsNoStore()
    {
        _storePropayClient
            .Setup(c => c.GetStoreIdFromPropayAccountNumberAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<GetStoreIdFromPropayAccountNumberResponse>(null!));

        var act = async () => await CreateResolver().Resolve(AccountNumber, CancellationToken.None);

        await act.Should().ThrowAsync<ResourceAccessException>();
    }

    [Fact]
    public async Task Resolve_ThrowsResourceAccess_WhenResolvedStoreIdIsNotPositive()
    {
        _storePropayClient
            .Setup(c => c.GetStoreIdFromPropayAccountNumberAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStoreIdFromPropayAccountNumberResponse { StoreId = 0, TenantId = 42 });

        var act = async () => await CreateResolver().Resolve(AccountNumber, CancellationToken.None);

        await act.Should().ThrowAsync<ResourceAccessException>();
    }
}
