using System.Text.Json.Serialization;
using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Messages;

/// <summary>
/// A transfer that had already been made came back — a payout the merchant's bank rejected, or a
/// funding transfer the payer's bank pulled back.
/// </summary>
/// <remarks>
/// <para>
/// Returns land days after the transfer they reverse, long after everything downstream has treated
/// the money as moved. That is what makes this event worth its own contract rather than a status on
/// the original: a consumer has to undo something it has already done, and tell someone.
/// </para>
/// <para>
/// When the returned transfer was a payout, <see cref="ProviderTransferRef"/> matches the
/// <see cref="PayoutCompleted.ProviderPayoutRef"/> of the payout being reversed. As with a payout,
/// a consumer may see a return for a transfer it never saw, so handle an unknown reference by
/// recording it and alerting rather than by discarding the event.
/// </para>
/// <para>
/// Sign convention: <see cref="Amount"/> is positive — the magnitude returned. Use
/// <see cref="Direction"/> to decide which way that moves a balance.
/// </para>
/// </remarks>
public sealed record TransferReturned : IStoreScopedEvent
{
    /// <inheritdoc cref="PaymentEvents.Source"/>
    public static string Source => PaymentEvents.Source;

    /// <summary>The bare event name: <c>"TransferReturned"</c>.</summary>
    public static string EventName => nameof(TransferReturned);

    /// <summary>Major version 1. Routed as <c>payments.TransferReturned.v1</c>.</summary>
    public static int Version => 1;

    /// <summary>
    /// Which provider reported the return. See <see cref="ProviderIds"/> — treat as opaque.
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// The provider's reference for the account the return is booked against, opaque to consumers.
    /// </summary>
    public required string ProviderAccountRef { get; init; }

    /// <summary>
    /// The provider's reference for the transfer that was returned, opaque to consumers. For a
    /// returned payout this is the <see cref="PayoutCompleted.ProviderPayoutRef"/> of that payout,
    /// which is how the two are correlated.
    /// </summary>
    public required string ProviderTransferRef { get; init; }

    /// <summary>Which way the original transfer was going. See <see cref="TransferDirection"/>.</summary>
    public required TransferDirection Direction { get; init; }

    /// <summary>The amount returned, positive.</summary>
    public required Money Amount { get; init; }

    /// <summary>
    /// When the return was booked by the provider. Distinct from
    /// <see cref="IntegrationMetadata.OccurredAt"/> on a report-derived record, and the date
    /// finance reconciles by.
    /// </summary>
    public required DateTimeOffset ReturnedAt { get; init; }

    /// <summary>
    /// The bank's return code, opaque and scheme-defined — an ACH return code such as
    /// <c>"R01"</c>, or the provider's equivalent. Carried so support can look up the exact cause
    /// and so returns can be reported by code; never branch on it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasonCode { get; init; }

    /// <summary>
    /// A short human-readable explanation of the return, for ops screens and merchant
    /// notifications. Not for parsing.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    /// <summary>
    /// The last four digits of the bank account at the other end, for display
    /// ("the transfer to the account ending 4821 was returned"). Never carry more of the account
    /// number than this.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CounterpartyLast4 { get; init; }

    /// <summary>
    /// The provider's reference for the payment this transfer funded, opaque to consumers. Present
    /// on an <see cref="TransferDirection.Inbound"/> return, where it is the join back to the order
    /// that is now unfunded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderPaymentRef { get; init; }

    /// <summary>
    /// Whether the producer learned this from a webhook, a report, or an operator. Deduplicate on
    /// <see cref="ProviderTransferRef"/> and let a <see cref="RecordOrigin.Report"/> record
    /// supersede a <see cref="RecordOrigin.Webhook"/> one.
    /// </summary>
    public required RecordOrigin Origin { get; init; }
}
