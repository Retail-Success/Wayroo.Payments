using AwesomeAssertions;
using Wayroo.Payments.BusinessLogic.Gateways.Propay;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.UnitTests.Gateways.Propay;

/// <summary>
/// Pins the ProPay-to-neutral status mapping. These groupings are a business decision, not a
/// mechanical translation, so each one is asserted by name rather than inferred.
/// </summary>
public class PropayAccountStatusMapTests
{
    /// <summary>
    /// Every value of <c>Luci.ProPay.Messages.AccountStatus</c>. Spelled out here rather than
    /// referenced so that this test keeps failing loudly if ProPay adds a status and the map is not
    /// updated — the whole point is to notice.
    /// </summary>
    private static readonly string[] EveryPropayStatus =
    [
        "AdditionalInformation", "Approved", "Canceled", "CheckPending", "CloseExcessiveChargeback",
        "Closed", "ClosedCollection", "ClosedEULA", "ClosedEscheated", "ClosedInaccurateData",
        "ClosedNoActivity", "DuplicateSSN", "ExceptionsFormRejected", "FraudAccount", "FraudVictim",
        "Hold", "Inactive", "InitialInformation", "OFACHold", "PendingRiskReview",
        "PendingRiskReviewUnpaid", "ReadyToProcess", "RiskWiseDeclined", "RiskWiseReady",
    ];

    [Theory]
    [InlineData("ReadyToProcess", PaymentAccountStatus.ReadyToProcess)]
    [InlineData("Approved", PaymentAccountStatus.ReadyToProcess)]
    [InlineData("RiskWiseReady", PaymentAccountStatus.ReadyToProcess)]
    [InlineData("AdditionalInformation", PaymentAccountStatus.ActionRequired)]
    [InlineData("InitialInformation", PaymentAccountStatus.ActionRequired)]
    [InlineData("CheckPending", PaymentAccountStatus.ActionRequired)]
    [InlineData("ExceptionsFormRejected", PaymentAccountStatus.ActionRequired)]
    [InlineData("PendingRiskReview", PaymentAccountStatus.Pending)]
    [InlineData("PendingRiskReviewUnpaid", PaymentAccountStatus.Pending)]
    [InlineData("Hold", PaymentAccountStatus.Suspended)]
    [InlineData("OFACHold", PaymentAccountStatus.Suspended)]
    [InlineData("Inactive", PaymentAccountStatus.Suspended)]
    [InlineData("RiskWiseDeclined", PaymentAccountStatus.Rejected)]
    [InlineData("DuplicateSSN", PaymentAccountStatus.Rejected)]
    [InlineData("FraudAccount", PaymentAccountStatus.Rejected)]
    [InlineData("FraudVictim", PaymentAccountStatus.Rejected)]
    [InlineData("Canceled", PaymentAccountStatus.Closed)]
    [InlineData("Closed", PaymentAccountStatus.Closed)]
    [InlineData("ClosedCollection", PaymentAccountStatus.Closed)]
    [InlineData("ClosedEULA", PaymentAccountStatus.Closed)]
    [InlineData("ClosedEscheated", PaymentAccountStatus.Closed)]
    [InlineData("ClosedInaccurateData", PaymentAccountStatus.Closed)]
    [InlineData("ClosedNoActivity", PaymentAccountStatus.Closed)]
    [InlineData("CloseExcessiveChargeback", PaymentAccountStatus.Closed)]
    public void Map_CollapsesEachPropayStatusOntoItsNeutralEquivalent(
        string propayStatus,
        PaymentAccountStatus expected)
    {
        PropayAccountStatusMap.Map(propayStatus).Should().Be(expected);
    }

    [Fact]
    public void Map_CoversEveryStatusPropayCanReport()
    {
        var unmapped = EveryPropayStatus.Where(status => PropayAccountStatusMap.Map(status) is null);

        unmapped.Should().BeEmpty("an unmapped status silently becomes Suspended and blocks a healthy store");
    }

    [Fact]
    public void KnownProviderStatuses_MatchesTheStatusesPropayCanReport()
    {
        PropayAccountStatusMap.KnownProviderStatuses.Should().BeEquivalentTo(EveryPropayStatus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SomethingProPayInventedLastTuesday")]
    public void Map_ReturnsNull_ForAnythingItDoesNotRecognise(string? providerStatus)
    {
        // Null rather than a guess: the caller decides what to do about it, and the gateway's
        // decision is to fail closed.
        PropayAccountStatusMap.Map(providerStatus).Should().BeNull();
    }

    [Theory]
    [InlineData("readytoprocess")]
    [InlineData("READYTOPROCESS")]
    [InlineData("  ReadyToProcess  ")]
    public void Map_ToleratesCasingAndSurroundingWhitespace(string providerStatus)
    {
        PropayAccountStatusMap.Map(providerStatus).Should().Be(PaymentAccountStatus.ReadyToProcess);
    }
}
