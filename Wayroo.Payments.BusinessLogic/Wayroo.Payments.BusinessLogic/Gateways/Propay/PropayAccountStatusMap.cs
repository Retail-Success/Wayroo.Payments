using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.Gateways.Propay;

/// <summary>
/// Collapses ProPay's account-status vocabulary onto the provider-neutral
/// <see cref="PaymentAccountStatus"/>.
/// </summary>
/// <remarks>
/// <para>
/// ProPay exposes two dozen statuses that mix lifecycle stage, risk posture and closure reason.
/// Consumers of this service branch on the six neutral values and on
/// <see cref="PaymentAccountBalance.CanProcessPayments"/> — never on ProPay's names, which is the
/// whole point of the seam. The provider's raw value still travels, as
/// <see cref="PaymentAccountDetails.ProviderStatusCode"/>, for support and diagnostics.
/// </para>
/// <para>
/// The groupings encode business judgement, not a mechanical translation — in particular whether a
/// status means "the merchant must act" or "we are waiting on the provider", and which closures are
/// recoverable. They match the set in <c>Luci.ProPay.Messages.AccountStatus</c>.
/// </para>
/// </remarks>
public static class PropayAccountStatusMap
{
    /// <summary>
    /// The ProPay status names, exactly as ProPay spells them, grouped by the neutral status each
    /// collapses onto. Compared case-insensitively.
    /// </summary>
    private static readonly Dictionary<string, PaymentAccountStatus> StatusesByProviderName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Verified and selling.
            ["ReadyToProcess"] = PaymentAccountStatus.ReadyToProcess,
            ["Approved"] = PaymentAccountStatus.ReadyToProcess,
            ["RiskWiseReady"] = PaymentAccountStatus.ReadyToProcess,

            // The provider is waiting on the merchant for something.
            ["AdditionalInformation"] = PaymentAccountStatus.ActionRequired,
            ["InitialInformation"] = PaymentAccountStatus.ActionRequired,
            ["CheckPending"] = PaymentAccountStatus.ActionRequired,
            ["ExceptionsFormRejected"] = PaymentAccountStatus.ActionRequired,

            // Under way, nothing required of the merchant.
            ["PendingRiskReview"] = PaymentAccountStatus.Pending,
            ["PendingRiskReviewUnpaid"] = PaymentAccountStatus.Pending,

            // Blocked, but recoverable.
            ["Hold"] = PaymentAccountStatus.Suspended,
            ["OFACHold"] = PaymentAccountStatus.Suspended,
            ["Inactive"] = PaymentAccountStatus.Suspended,

            // Failed verification terminally; a new account is required.
            ["RiskWiseDeclined"] = PaymentAccountStatus.Rejected,
            ["DuplicateSSN"] = PaymentAccountStatus.Rejected,
            ["FraudAccount"] = PaymentAccountStatus.Rejected,
            ["FraudVictim"] = PaymentAccountStatus.Rejected,

            // Closed. ProPay distinguishes why; consumers do not need to.
            ["Canceled"] = PaymentAccountStatus.Closed,
            ["Closed"] = PaymentAccountStatus.Closed,
            ["ClosedCollection"] = PaymentAccountStatus.Closed,
            ["ClosedEULA"] = PaymentAccountStatus.Closed,
            ["ClosedEscheated"] = PaymentAccountStatus.Closed,
            ["ClosedInaccurateData"] = PaymentAccountStatus.Closed,
            ["ClosedNoActivity"] = PaymentAccountStatus.Closed,
            // Not a typo on ProPay's side — they hit a character limit.
            ["CloseExcessiveChargeback"] = PaymentAccountStatus.Closed,
        };

    /// <summary>Every ProPay status name this map recognises.</summary>
    public static IReadOnlyCollection<string> KnownProviderStatuses => StatusesByProviderName.Keys;

    /// <summary>
    /// Maps a ProPay account status onto its neutral equivalent.
    /// </summary>
    /// <param name="providerStatus">The status exactly as ProPay reported it.</param>
    /// <returns>
    /// The neutral status, or <c>null</c> when <paramref name="providerStatus"/> is blank or
    /// unrecognised. A caller that needs a definite answer should treat <c>null</c> as
    /// <see cref="PaymentAccountStatus.Suspended"/> and refuse to sell — see
    /// <see cref="PropayAccountGateway"/>.
    /// </returns>
    public static PaymentAccountStatus? Map(string? providerStatus)
        => !string.IsNullOrWhiteSpace(providerStatus)
           && StatusesByProviderName.TryGetValue(providerStatus.Trim(), out var status)
            ? status
            : null;
}
