using System.Text.Json.Serialization;
using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Messages;

/// <summary>
/// An open dispute moved: evidence was submitted, liability was accepted, or the issuer decided.
/// </summary>
/// <remarks>
/// <para>
/// Correlate to the opening <see cref="DisputeOpened"/> on <see cref="ProviderDisputeRef"/>. A
/// consumer may legitimately see this event for a dispute it never saw opened — a replay, a
/// backfill, or a case opened before the consumer was deployed — so handle an unknown reference by
/// recording the case rather than discarding the event.
/// </para>
/// <para>
/// Sign convention: <see cref="Amount"/> is signed relative to the store's balance — positive when
/// funds are returned to the store (a win or a withdrawal), negative when funds are taken (a loss
/// settling a debit that had not yet been applied).
/// </para>
/// </remarks>
public sealed record DisputeStatusChanged : IStoreScopedEvent
{
    /// <inheritdoc cref="PaymentEvents.Source"/>
    public static string Source => PaymentEvents.Source;

    /// <summary>The bare event name: <c>"DisputeStatusChanged"</c>.</summary>
    public static string EventName => nameof(DisputeStatusChanged);

    /// <summary>Major version 1. Routed as <c>payments.DisputeStatusChanged.v1</c>.</summary>
    public static int Version => 1;

    /// <summary>
    /// Which provider reported the change. See <see cref="ProviderIds"/> — treat as opaque.
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// The provider's reference for the account the dispute is booked against, opaque to consumers.
    /// </summary>
    public required string ProviderAccountRef { get; init; }

    /// <summary>
    /// The provider's reference for the dispute case, opaque to consumers. Matches the
    /// <see cref="DisputeOpened.ProviderDisputeRef"/> of the case this event belongs to.
    /// </summary>
    public required string ProviderDisputeRef { get; init; }

    /// <summary>
    /// The provider's reference for the disputed payment, opaque to consumers. Repeated from
    /// <see cref="DisputeOpened"/> so a consumer that never saw the opening event can still resolve
    /// the order.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderPaymentRef { get; init; }

    /// <summary>The dispute's standing as of <see cref="IntegrationMetadata.OccurredAt"/>.</summary>
    public required DisputeStatus Status { get; init; }

    /// <summary>
    /// The standing immediately before, when the producer knows it. Present so consumers can react
    /// to a transition rather than to a level; null when the producer did not have the prior state.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DisputeStatus? PreviousStatus { get; init; }

    /// <summary>
    /// The funds this transition moved, signed against the store's balance — see the remarks on
    /// <see cref="DisputeStatusChanged"/>. Null when the transition moved no money, such as
    /// entering <see cref="DisputeStatus.Defended"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Money? Amount { get; init; }

    /// <summary>
    /// When the dispute reached a terminal status, set only on
    /// <see cref="DisputeStatus.Won"/>, <see cref="DisputeStatus.Lost"/>,
    /// <see cref="DisputeStatus.Accepted"/>, <see cref="DisputeStatus.Expired"/> and
    /// <see cref="DisputeStatus.Withdrawn"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ResolvedAt { get; init; }

    /// <summary>
    /// The evidence deadline as it now stands. Repeated on every change because providers do extend
    /// it — trust the latest value over the one carried by <see cref="DisputeOpened"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? DefenseDueBy { get; init; }

    /// <summary>
    /// The provider's or network's code explaining the outcome, opaque and provider-defined.
    /// Carried for support and win-rate analysis; never branch on it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasonCode { get; init; }

    /// <summary>
    /// A short human-readable explanation of the outcome, for ops screens and merchant
    /// notifications. Not for parsing.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
}
