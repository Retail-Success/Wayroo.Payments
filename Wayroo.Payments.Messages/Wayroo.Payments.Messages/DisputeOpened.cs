using System.Text.Json.Serialization;
using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Messages;

/// <summary>
/// A cardholder has disputed a payment. Money may already have been taken back, and the merchant is
/// on a clock to respond.
/// </summary>
/// <remarks>
/// <para>
/// This event opens the case: it drives the merchant notification, the ops review queue, and the
/// chargeback record written against the order. Subsequent movement — evidence submitted, decision
/// reached — arrives as <see cref="DisputeStatusChanged"/> carrying the same
/// <see cref="ProviderDisputeRef"/>.
/// </para>
/// <para>
/// One dispute can produce several provider-side notifications when a payment was split across
/// accounts. The producer correlates those into a single logical dispute, so consumers can rely on
/// <see cref="ProviderDisputeRef"/> identifying one case.
/// </para>
/// <para>
/// Sign convention: <see cref="DisputedAmount"/> is positive — the magnitude being disputed.
/// </para>
/// </remarks>
public sealed record DisputeOpened : IStoreScopedEvent
{
    /// <inheritdoc cref="PaymentEvents.Source"/>
    public static string Source => PaymentEvents.Source;

    /// <summary>The bare event name: <c>"DisputeOpened"</c>.</summary>
    public static string EventName => nameof(DisputeOpened);

    /// <summary>Major version 1. Routed as <c>payments.DisputeOpened.v1</c>.</summary>
    public static int Version => 1;

    /// <summary>
    /// Which provider reported the dispute. See <see cref="ProviderIds"/> — treat as opaque.
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// The provider's reference for the account the dispute is booked against, opaque to consumers.
    /// </summary>
    public required string ProviderAccountRef { get; init; }

    /// <summary>
    /// The provider's reference for the dispute case, opaque to consumers. Stable for the life of
    /// the case: the key to deduplicate on, to correlate <see cref="DisputeStatusChanged"/> to, and
    /// to pass back to the provider when submitting evidence.
    /// </summary>
    public required string ProviderDisputeRef { get; init; }

    /// <summary>
    /// The provider's reference for the disputed payment, opaque to consumers. The join back to the
    /// order that is being charged back.
    /// </summary>
    public required string ProviderPaymentRef { get; init; }

    /// <summary>How far the dispute has escalated, and therefore what is being asked for.</summary>
    public required DisputeType DisputeType { get; init; }

    /// <summary>
    /// The amount under dispute, positive. May be less than the original payment on a partial
    /// dispute, so never assume it equals the order total.
    /// </summary>
    public required Money DisputedAmount { get; init; }

    /// <summary>
    /// When the dispute was raised by the issuer. Distinct from
    /// <see cref="IntegrationMetadata.OccurredAt"/>, which is when the platform found out — the gap
    /// matters, because the response deadline runs from the former.
    /// </summary>
    public required DateTimeOffset OpenedAt { get; init; }

    /// <summary>
    /// The deadline for submitting evidence, when the provider gave one. <b>Missing it loses the
    /// dispute by default</b>, so this drives the ops queue's ordering and its escalation alerts.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? DefenseDueBy { get; init; }

    /// <summary>
    /// Whether the disputed funds have already been debited from the account. False for an
    /// <see cref="DisputeType.Inquiry"/>, where nothing has moved yet; true for a
    /// chargeback, where the balance is already down and the merchant's available funds are
    /// affected today.
    /// </summary>
    public required bool FundsDebited { get; init; }

    /// <summary>
    /// The card network's reason code for the dispute, opaque and network-defined. Carried for
    /// support and for win-rate analysis by reason; never branch on it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasonCode { get; init; }

    /// <summary>
    /// A short human-readable description of why the payment was disputed, for ops screens and
    /// merchant notifications. Not for parsing.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    /// <summary>
    /// The platform's own reference for the disputed payment, echoed back by the provider when it
    /// carries one — typically the order number. A convenience for support; not the match key.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OrderReference { get; init; }
}
