using AwesomeAssertions;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.UnitTests;

/// <summary>
/// The idempotency key sent to Adyen's balance platform.
/// </summary>
/// <remarks>
/// Everything worth testing here is about <i>sameness</i>. The key is what makes a retry replay the
/// account holder a first attempt created rather than open a second one, so two calls that are the
/// same operation must produce identical text, and two calls that are not must never collide.
/// </remarks>
public class AdyenIdempotencyKeyTests
{
    private const long StoreId = 31610;

    [Fact]
    public void AnAccountHolderKey_IsTheAgreedFormat()
        => AdyenIdempotencyKey.ForAccountHolder(AdyenReferenceEnvironment.Prod, StoreId, 1)
            .ToString()
            .Should()
            .Be("wayroo-prod-31610-accountholder-g1");

    [Fact]
    public void ABalanceAccountKey_IsTheAgreedFormat()
        => AdyenIdempotencyKey.ForBalanceAccount(AdyenReferenceEnvironment.Prod, StoreId, 1)
            .ToString()
            .Should()
            .Be("wayroo-prod-31610-balanceaccount-g1");

    /// <summary>
    /// The property the whole design rests on: the key is derived, not generated, so a retry in a
    /// later process — after a redeploy, on another host — reproduces it exactly.
    /// </summary>
    [Fact]
    public void TheSameOperation_ProducesTheSameKeyEveryTime()
        => AdyenIdempotencyKey.ForAccountHolder(AdyenReferenceEnvironment.Dev, StoreId, 3)
            .Should()
            .Be(AdyenIdempotencyKey.ForAccountHolder(AdyenReferenceEnvironment.Dev, StoreId, 3));

    /// <summary>
    /// Two rungs of one store's onboarding are different operations. Sharing a key would have the
    /// balance-account call answered with the account holder.
    /// </summary>
    [Fact]
    public void TheTwoRungs_DoNotShareAKey()
        => AdyenIdempotencyKey.ForAccountHolder(AdyenReferenceEnvironment.Dev, StoreId, 1)
            .Should()
            .NotBe(AdyenIdempotencyKey.ForBalanceAccount(AdyenReferenceEnvironment.Dev, StoreId, 1));

    /// <summary>
    /// The reason the generation exists. Re-onboarding a store deliberately has to create a new
    /// account holder; replaying the old key would hand back the one the store is being moved off.
    /// </summary>
    [Fact]
    public void ALaterGeneration_DoesNotReplayAnEarlierOne()
        => AdyenIdempotencyKey.ForAccountHolder(AdyenReferenceEnvironment.Dev, StoreId, 2)
            .Should()
            .NotBe(AdyenIdempotencyKey.ForAccountHolder(AdyenReferenceEnvironment.Dev, StoreId, 1));

    /// <summary>
    /// Dev and QA run against one shared Adyen test platform, where keys are held per company
    /// account. Without the environment, QA onboarding store 31610 would replay dev's account holder.
    /// </summary>
    [Fact]
    public void DevAndQa_DoNotShareAKey()
        => AdyenIdempotencyKey.ForAccountHolder(AdyenReferenceEnvironment.Dev, StoreId, 1)
            .Should()
            .NotBe(AdyenIdempotencyKey.ForAccountHolder(AdyenReferenceEnvironment.Qa, StoreId, 1));

    [Fact]
    public void DifferentStores_DoNotShareAKey()
        => AdyenIdempotencyKey.ForAccountHolder(AdyenReferenceEnvironment.Dev, StoreId, 1)
            .Should()
            .NotBe(AdyenIdempotencyKey.ForAccountHolder(AdyenReferenceEnvironment.Dev, StoreId + 1, 1));

    /// <summary>
    /// Every key fits Adyen's header limit, including for a store identifier far larger than any
    /// issued today.
    /// </summary>
    [Theory]
    [InlineData(1L)]
    [InlineData(31610L)]
    [InlineData(long.MaxValue)]
    public void EveryKey_FitsAdyensHeaderLimit(long storeId)
        => AdyenIdempotencyKey.ForBalanceAccount(AdyenReferenceEnvironment.Prod, storeId, 1)
            .ToString()
            .Length
            .Should()
            .BeLessThanOrEqualTo(AdyenIdempotencyKey.MaxLength);

    /// <summary>
    /// A key too long for the header is refused here rather than sent and rejected there, because
    /// Adyen rejecting it would fail the create the key exists to protect — and the caller would read
    /// that as the provider declining to open the account.
    /// </summary>
    [Fact]
    public void AKeyTooLongForTheHeader_IsRefusedRatherThanSent()
    {
        var build = () => AdyenIdempotencyKey.ForBalanceAccount(
            AdyenReferenceEnvironment.Prod,
            long.MaxValue,
            long.MaxValue);

        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// A key naming no store is not a weaker guard, it is a guard against the wrong thing: it would
    /// be shared by every store onboarding on that rung.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void AKeyCannotBeBuiltWithoutAStore(long storeId)
    {
        var build = () => AdyenIdempotencyKey.ForAccountHolder(AdyenReferenceEnvironment.Dev, storeId, 1);

        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Generations start at one. A zero would make a store's first attempt and some later reset share
    /// a key.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void AKeyCannotBeBuiltWithoutAGeneration(long generation)
    {
        var build = () => AdyenIdempotencyKey.ForAccountHolder(AdyenReferenceEnvironment.Dev, StoreId, generation);

        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Keys are compared and sent as text, and the namespace is what keeps ours from colliding with
    /// another integration's on the same company account.
    /// </summary>
    [Fact]
    public void EveryKey_IsNamespacedAndLowercase()
    {
        var key = AdyenIdempotencyKey.ForBalanceAccount(AdyenReferenceEnvironment.Qa, StoreId, 1).ToString();

        key.Should().StartWith("wayroo-");
        key.Should().Be(key.ToLowerInvariant());
    }
}
