using System.Text.Json.Serialization;
using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Messages;

/// <summary>
/// A captured payment has settled: the provider has booked the money, and the fees it actually
/// charged are now known.
/// </summary>
/// <remarks>
/// <para>
/// Authorization and capture say what <i>should</i> happen; settlement says what did. This is the
/// event the order ledger and the reconciliation pipeline key off — matching
/// <see cref="ProviderPaymentRef"/> back to the recorded payment, writing the fee actuals, and,
/// where a payment was never marked paid, acting as the reconciliation-driven fallback that marks
/// the order paid.
/// </para>
/// <para>
/// The same settlement is normally reported twice: once quickly from a webhook and once
/// authoritatively from the daily report. Both are published, distinguished by
/// <see cref="Origin"/>, so consumers see the money promptly and reconcile against the complete
/// figures later. A handler must therefore be idempotent per <see cref="ProviderPaymentRef"/> and
/// let a <see cref="RecordOrigin.Report"/> record supersede a <see cref="RecordOrigin.Webhook"/>
/// one for the same reference.
/// </para>
/// <para>
/// Sign convention: <see cref="GrossAmount"/> and <see cref="NetAmount"/> are positive for a sale
/// and negative for a refund. <see cref="FeeAmount"/> is positive as charged.
/// </para>
/// </remarks>
public sealed record PaymentSettlementRecorded : IStoreScopedEvent
{
    /// <inheritdoc cref="PaymentEvents.Source"/>
    public static string Source => PaymentEvents.Source;

    /// <summary>The bare event name: <c>"PaymentSettlementRecorded"</c>.</summary>
    public static string EventName => nameof(PaymentSettlementRecorded);

    /// <summary>Major version 1. Routed as <c>payments.PaymentSettlementRecorded.v1</c>.</summary>
    public static int Version => 1;

    /// <summary>
    /// Which provider settled the payment. See <see cref="ProviderIds"/> — treat as opaque.
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// The provider's reference for the account the money booked to, opaque to consumers.
    /// </summary>
    public required string ProviderAccountRef { get; init; }

    /// <summary>
    /// The provider's reference for the payment, opaque to consumers. <b>This is the match key</b>
    /// — the value the platform stored when it took the payment, and the join back to the order.
    /// </summary>
    public required string ProviderPaymentRef { get; init; }

    /// <summary>
    /// The provider's reference for the specific capture or refund being settled, when it differs
    /// from <see cref="ProviderPaymentRef"/>. Present where a provider issues a distinct reference
    /// per modification; use it to tell two settlements of the same payment apart.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderCaptureRef { get; init; }

    /// <summary>
    /// The platform's own reference submitted with the payment, echoed back by the provider when it
    /// carries one — typically the order number. A convenience for support and for matching when
    /// <see cref="ProviderPaymentRef"/> did not resolve; not a substitute for it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OrderReference { get; init; }

    /// <summary>
    /// When the provider booked the money. Distinct from <see cref="IntegrationMetadata.OccurredAt"/>
    /// on a report-derived record, which is published a day or more after the booking date, and the
    /// value finance reconciles by.
    /// </summary>
    public required DateTimeOffset SettledAt { get; init; }

    /// <summary>The transaction amount before fees. Negative for a refund.</summary>
    public required Money GrossAmount { get; init; }

    /// <summary>
    /// Total fees charged against this transaction, positive as charged. Equals the sum of
    /// <see cref="Fees"/> when the breakdown is present.
    /// </summary>
    public required Money FeeAmount { get; init; }

    /// <summary>
    /// What actually reached the account: <see cref="GrossAmount"/> less <see cref="FeeAmount"/>
    /// and less any platform commission split away from the store.
    /// </summary>
    public required Money NetAmount { get; init; }

    /// <summary>
    /// The platform's commission on this transaction — the share split to the platform's own
    /// account rather than the store's. Null when the provider did not report a split for this
    /// transaction, which is not the same as a commission of zero.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Money? CommissionAmount { get; init; }

    /// <summary>
    /// The per-fee breakdown behind <see cref="FeeAmount"/>. Typically present only on
    /// <see cref="RecordOrigin.Report"/> records, since a webhook rarely carries it. Absent means
    /// "not reported", never "no fees".
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SettlementFee>? Fees { get; init; }

    /// <summary>
    /// The provider's reference for the settlement batch or report this record came from, opaque to
    /// consumers. Ties a row back to the file it was parsed out of when a figure is questioned.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderBatchRef { get; init; }

    /// <summary>
    /// Whether the producer learned this from a webhook, a report, or an operator. Governs which
    /// record wins when the same settlement arrives twice — see the remarks on
    /// <see cref="PaymentSettlementRecorded"/>.
    /// </summary>
    public required RecordOrigin Origin { get; init; }
}
