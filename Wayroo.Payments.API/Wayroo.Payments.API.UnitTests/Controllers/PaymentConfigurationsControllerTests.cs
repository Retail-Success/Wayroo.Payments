using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Wayroo.Payments.API.Controllers;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.API.UnitTests.Controllers;

public class PaymentConfigurationsControllerTests
{
    private readonly Mock<ILogger<PaymentConfigurationsController>> _logger = new();
    private readonly Mock<IPaymentConfigurationRepository> _repository = new();
    private readonly PaymentConfigurationsController _controller;

    public PaymentConfigurationsControllerTests()
    {
        _controller = new PaymentConfigurationsController(_logger.Object, _repository.Object);
    }

    [Fact]
    public async Task GetConfigurationsForStore_ReturnsOkWithListFromRepository()
    {
        const long storeId = 31144;
        var expected = new List<PaymentProviderConfiguration>
        {
            new() { StoreId = storeId, ProviderId = "propay", AccountId = "acct-1" },
            new() { StoreId = storeId, ProviderId = "stripe", AccountId = "acct-2" },
        };
        _repository
            .Setup(r => r.GetConfigurationsForStore(storeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _controller.GetConfigurationsForStore(storeId, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task GetConfigurationsForStore_PassesStoreIdToRepository()
    {
        const long storeId = 31144;
        _repository
            .Setup(r => r.GetConfigurationsForStore(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _controller.GetConfigurationsForStore(storeId, CancellationToken.None);

        _repository.Verify(
            r => r.GetConfigurationsForStore(storeId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetConfiguration_WhenRepositoryReturnsRow_ReturnsOk()
    {
        const long storeId = 31144;
        const string providerId = "propay";
        var expected = new PaymentProviderConfiguration
        {
            StoreId = storeId,
            ProviderId = providerId,
            AccountId = "acct-1",
            ProviderConfiguration = "{\"fake\":\"payload\"}",
        };
        _repository
            .Setup(r => r.GetConfiguration(storeId, providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _controller.GetConfiguration(storeId, providerId, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task GetConfiguration_WhenRepositoryReturnsNull_ReturnsNotFound()
    {
        const long storeId = 31144;
        const string providerId = "propay";
        _repository
            .Setup(r => r.GetConfiguration(storeId, providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentProviderConfiguration?)null);

        var result = await _controller.GetConfiguration(storeId, providerId, CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetConfiguration_PassesRouteParametersToRepository()
    {
        const long storeId = 31144;
        const string providerId = "propay";
        _repository
            .Setup(r => r.GetConfiguration(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentProviderConfiguration?)null);

        await _controller.GetConfiguration(storeId, providerId, CancellationToken.None);

        _repository.Verify(
            r => r.GetConfiguration(storeId, providerId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
