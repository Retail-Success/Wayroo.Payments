using System.Net;
using AwesomeAssertions;
using Luci.Orders.SDK.Api;
using Luci.Orders.SDK.Models.StorePropay;
using Moq;
using Refit;
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

    [Fact]
    public async Task Resolve_ThrowsResourceAccess_WhenOrdersCallFailsUnexpectedly()
    {
        var sdkFailure = new HttpRequestException("Orders API unavailable");
        _storePropayClient
            .Setup(c => c.GetStoreIdFromPropayAccountNumberAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(sdkFailure);

        var act = async () => await CreateResolver().Resolve(AccountNumber, CancellationToken.None);

        // Unexpected SDK failures are reclassified as transient (re-queue) and preserve the original
        // exception so ProcessFailureHandler can log it as a warning.
        var thrown = await act.Should().ThrowAsync<ResourceAccessException>();
        thrown.Which.InnerException.Should().BeSameAs(sdkFailure);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)] // 500 — server-side, retry
    [InlineData(HttpStatusCode.BadGateway)]          // 502 — server-side, retry
    [InlineData(HttpStatusCode.ServiceUnavailable)]  // 503 — server-side, retry
    [InlineData(HttpStatusCode.RequestTimeout)]      // 408 — worth retrying
    [InlineData(HttpStatusCode.TooManyRequests)]     // 429 — worth retrying after backoff
    public async Task Resolve_ThrowsResourceAccess_WhenOrdersReturnsRetryableStatus(HttpStatusCode statusCode)
    {
        var sdkFailure = await CreateApiException(statusCode);
        _storePropayClient
            .Setup(c => c.GetStoreIdFromPropayAccountNumberAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(sdkFailure);

        var act = async () => await CreateResolver().Resolve(AccountNumber, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ResourceAccessException>();
        thrown.Which.InnerException.Should().BeSameAs(sdkFailure);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]   // 400
    [InlineData(HttpStatusCode.Unauthorized)] // 401
    [InlineData(HttpStatusCode.Forbidden)]    // 403
    [InlineData(HttpStatusCode.NotFound)]     // 404
    [InlineData(HttpStatusCode.Conflict)]     // 409
    public async Task Resolve_PropagatesApiException_WhenOrdersReturnsPermanentClientError(HttpStatusCode statusCode)
    {
        var sdkFailure = await CreateApiException(statusCode);
        _storePropayClient
            .Setup(c => c.GetStoreIdFromPropayAccountNumberAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(sdkFailure);

        var act = async () => await CreateResolver().Resolve(AccountNumber, CancellationToken.None);

        // Permanent client errors are NOT reclassified as transient: the original ApiException
        // propagates unchanged so ProcessFailureHandler dead-letters the message.
        var thrown = await act.Should().ThrowAsync<ApiException>();
        thrown.Which.Should().BeSameAs(sdkFailure);
    }

    private static async Task<ApiException> CreateApiException(HttpStatusCode statusCode)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://orders.test/propay/accounts/{AccountNumber}/store");
        using var response = new HttpResponseMessage(statusCode) { RequestMessage = request };
        return await ApiException.Create(request, HttpMethod.Get, response, new RefitSettings());
    }
}
