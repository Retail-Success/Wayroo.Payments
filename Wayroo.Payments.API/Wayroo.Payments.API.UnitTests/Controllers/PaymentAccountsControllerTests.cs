using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Wayroo.Payments.API.Controllers;
using Wayroo.Payments.BusinessLogic.Managers;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.API.UnitTests.Controllers;

/// <summary>
/// The controller is HTTP plumbing only — provider resolution and the "no account" answer belong to
/// <see cref="IPaymentAccountManager"/> and are tested against it. What is left to prove here is that
/// route and query values reach the manager and its result is returned unaltered.
/// </summary>
public class PaymentAccountsControllerTests
{
    private const long TenantId = 42;
    private const long StoreId = 31144;

    private readonly Mock<IPaymentAccountManager> _accountManager = new();
    private readonly PaymentAccountsController _controller;

    public PaymentAccountsControllerTests() => _controller = new PaymentAccountsController(_accountManager.Object);

    [Fact]
    public async Task GetAccountBalance_ReturnsWhatTheManagerRead()
    {
        var expected = new PaymentAccountBalance
        {
            AccountExists = true,
            ProviderId = "propay",
            AvailableBalance = MoneyAmount.Usd(1500.50m),
            Status = PaymentAccountStatus.ReadyToProcess,
            CanProcessPayments = true,
        };
        _accountManager
            .Setup(m => m.GetBalance(TenantId, StoreId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _controller.GetAccountBalance(TenantId, StoreId, null, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task RefreshAccount_ReturnsWhatTheManagerRecorded()
    {
        var expected = new PaymentAccountDetails
        {
            AccountExists = true,
            ProviderId = "propay",
            Status = PaymentAccountStatus.ReadyToProcess,
        };
        _accountManager
            .Setup(m => m.RefreshAccount(TenantId, StoreId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _controller.RefreshAccount(TenantId, StoreId, null, request: null, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeSameAs(expected);
    }

    /// <summary>
    /// The "no account" result is an ordinary 200 carrying a flag, not a 404 — the controller must
    /// pass it straight through rather than reinterpreting it.
    /// </summary>
    [Fact]
    public async Task GetAccountBalance_PassesThroughTheNoAccountResult()
    {
        _accountManager
            .Setup(m => m.GetBalance(TenantId, StoreId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentAccountBalance { AccountExists = false, ProviderId = "propay" });

        var result = await _controller.GetAccountBalance(TenantId, StoreId, null, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<PaymentAccountBalance>().Which.AccountExists.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("propay")]
    public async Task GetAccountBalance_ForwardsTheOptionalProviderOverride(string? providerId)
    {
        _accountManager
            .Setup(m => m.GetBalance(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentAccountBalance());

        await _controller.GetAccountBalance(TenantId, StoreId, providerId, CancellationToken.None);

        _accountManager.Verify(
            m => m.GetBalance(TenantId, StoreId, providerId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("propay")]
    public async Task RefreshAccount_ForwardsTheOptionalProviderOverride(string? providerId)
    {
        _accountManager
            .Setup(m => m.RefreshAccount(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentAccountDetails());

        await _controller.RefreshAccount(TenantId, StoreId, providerId, request: null, CancellationToken.None);

        _accountManager.Verify(
            m => m.RefreshAccount(TenantId, StoreId, providerId, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshAccount_ForwardsTheAccountReferenceFromTheBody()
    {
        _accountManager
            .Setup(m => m.RefreshAccount(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentAccountDetails());

        await _controller.RefreshAccount(
            TenantId,
            StoreId,
            providerId: null,
            new RefreshPaymentAccountRequest { ProviderAccountRef = "718040110898" },
            CancellationToken.None);

        _accountManager.Verify(
            m => m.RefreshAccount(TenantId, StoreId, null, "718040110898", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
