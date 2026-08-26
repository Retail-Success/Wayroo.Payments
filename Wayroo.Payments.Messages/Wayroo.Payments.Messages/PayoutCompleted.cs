using System.Text.Json.Serialization;
using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Messages;

/// <summary>
/// Funds left a store's balance with the provider and are on their way to the merchant's bank.
/// </summary>
/// <remarks>
/// <para>
/// "Completed" means the provider has executed the payout, not that the money has cleared the
/// merchant's bank. A payout can still come back days later as a return, which arrives as a
/// separate <see cref="TransferReturned"/> carrying the same reference in
/// <see cref="TransferReturned.ProviderTransferRef"/> — so a consumer must not treat this event as
/// final settlement of the merchant's balance.
/// </para>
/// <para>
/// Sign convention: <see cref="Amount"/> is positive — the magnitude paid out.
/// </para>
/// </remarks>
public sealed record PayoutCompleted : IStoreScopedEvent
{
    /// <inheritdoc cref="PaymentEvents.Source"/>
    public static string Source => PaymentEvents.Source;

    /// <summary>The bare event name: <c>"PayoutCompleted"</c>.</summary>
    public static string EventName => nameof(PayoutCompleted);

    /// <summary>Major version 1. Routed as <c>payments.PayoutCompleted.v1</c>.</summary>
    public static int Version => 1;

    /// <summary>
    /// Which provider made the payout. See <see cref="ProviderIds"/> — treat as opaque.
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// The provider's reference for the account the funds were paid out from, opaque to consumers.
    /// </summary>
    public required string ProviderAccountRef { get; init; }

    /// <summary>
    /// The provider's reference for this payout, opaque to consumers. The idempotency key for a
    /// payout-history read model, and the correlation key a later
    /// <see cref="TransferReturned"/> carries if the payout is reversed.
    /// </summary>
    public required string ProviderPayoutRef { get; init; }

    /// <summary>The amount paid out, positive.</summary>
    public required Money Amount { get; init; }

    /// <summary>
    /// When the provider executed the payout. Distinct from
    /// <see cref="IntegrationMetadata.OccurredAt"/> on a report-derived record, and the date to
    /// show a merchant.
    /// </summary>
    public required DateTimeOffset PaidOutAt { get; init; }

    /// <summary>
    /// When the funds are expected to land in the merchant's bank, when the provider estimates it.
    /// An estimate, not a commitment — say so wherever it is shown to a merchant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpectedArrivalAt { get; init; }

    /// <summary>
    /// The provider's reference for the bank account or transfer instrument paid to, opaque to
    /// consumers. Distinguishes payouts when a merchant has more than one destination on file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DestinationRef { get; init; }

    /// <summary>
    /// The last four digits of the destination bank account, for merchant-facing display
    /// ("paid to account ending 4821"). Never carry more of the account number than this.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DestinationLast4 { get; init; }

    /// <summary>
    /// The provider's reference for the payout batch or report this record came from, opaque to
    /// consumers. Ties a payout back to the file it was reconciled from.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderBatchRef { get; init; }

    /// <summary>
    /// Whether the producer learned this from a webhook, a report, or an operator. The same payout
    /// is normally reported from both a webhook and the payout report; deduplicate on
    /// <see cref="ProviderPayoutRef"/> and let the report record supersede the webhook one.
    /// </summary>
    public required RecordOrigin Origin { get; init; }
}
