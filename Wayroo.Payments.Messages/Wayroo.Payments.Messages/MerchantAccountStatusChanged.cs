using System.Text.Json.Serialization;
using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Messages;

/// <summary>
/// A store's merchant account changed standing with its payment provider — it finished onboarding,
/// needs something from the merchant, was suspended, was rejected, or was closed.
/// </summary>
/// <remarks>
/// <para>
/// This is the neutral replacement for provider-specific account-status notifications. It drives
/// store enablement, the merchant lifecycle emails, the ERP push of the merchant's account
/// reference, and the merchant-configuration read models that decide whether a store can sell.
/// </para>
/// <para>
/// The event is a <i>statement of current standing</i>, not a delta, so a consumer that missed
/// earlier events still converges on the right state from the latest one. Because delivery is
/// at-least-once and unordered, handlers should deduplicate on
/// <see cref="IntegrationMetadata.EventId"/> and drop anything whose
/// <see cref="IntegrationMetadata.Sequence"/> they have already applied.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var message = IntegrationJson.DeserializeMessage&lt;MerchantAccountStatusChanged&gt;(sqsMessage.Body);
/// MerchantAccountStatusChanged status = message.Detail.Data;
///
/// if (status.Status == MerchantAccountStatus.ReadyToProcess)
/// {
///     await _stores.EnableAsync(
///         message.Detail.Metadata.Scope.RequireStoreId(), status.ProviderAccountRef, ct);
/// }
/// </code>
/// </example>
public sealed record MerchantAccountStatusChanged : IStoreScopedEvent
{
    /// <inheritdoc cref="PaymentEvents.Source"/>
    public static string Source => PaymentEvents.Source;

    /// <summary>The bare event name: <c>"MerchantAccountStatusChanged"</c>.</summary>
    public static string EventName => nameof(MerchantAccountStatusChanged);

    /// <summary>Major version 1. Routed as <c>payments.MerchantAccountStatusChanged.v1</c>.</summary>
    public static int Version => 1;

    /// <summary>
    /// Which provider the account lives with. See <see cref="ProviderIds"/> — treat as opaque.
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// The provider's reference for the merchant account, opaque to consumers. Stable for the life
    /// of the account, so it is the right key for a per-provider read model and the value pushed to
    /// the ERP as the store's payout account reference.
    /// </summary>
    public required string ProviderAccountRef { get; init; }

    /// <summary>The account's standing as of <see cref="IntegrationMetadata.OccurredAt"/>.</summary>
    public required MerchantAccountStatus Status { get; init; }

    /// <summary>
    /// The standing this account was in immediately before, when the producer knows it. Present so
    /// consumers can react to a transition (send an email on entering a state) rather than to a
    /// level. Null on the first event for an account, or after a producer restart lost the prior
    /// state — never treat a null as "no change".
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MerchantAccountStatus? PreviousStatus { get; init; }

    /// <summary>
    /// Whether the store can take payments right now. This is the field that gates selling — not
    /// <see cref="Status"/>.
    /// </summary>
    /// <remarks>
    /// The two disagree during a grace period: an account can be <see cref="MerchantAccountStatus.ActionRequired"/>
    /// and still processing until <see cref="ActionRequiredBy"/>. Turning a store off the moment
    /// something is requested would take revenue away from a merchant the provider is still happy
    /// to process for. This maps onto the neutral card-processing status carried on the Orders
    /// merchant-configuration contract.
    /// </remarks>
    public required bool CanProcessPayments { get; init; }

    /// <summary>
    /// Whether funds can currently be paid out to the merchant's bank. Independent of
    /// <see cref="CanProcessPayments"/>: an account can keep taking money while payouts are held,
    /// and the balance accumulates until it is released.
    /// </summary>
    public required bool CanReceivePayouts { get; init; }

    /// <summary>
    /// When the outstanding action stops being optional, if the provider gave a deadline. After it
    /// passes, expect a further event moving the account out of
    /// <see cref="MerchantAccountStatus.ActionRequired"/>. Surface it to the merchant and to ops —
    /// it is the whole content of the warning.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ActionRequiredBy { get; init; }

    /// <summary>
    /// A short human-readable explanation of the current standing, suitable for an ops screen or a
    /// support conversation. Not for parsing, and not guaranteed stable across providers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    /// <summary>
    /// The provider's own code for that explanation, opaque and provider-defined. Carried so
    /// support can quote it back to the provider; never branch on it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasonCode { get; init; }

    /// <summary>
    /// The per-capability detail behind the status: provider-defined capability names mapped to
    /// provider-defined states, e.g. <c>{ "receivePayments": "valid", "sendToTransferInstrument":
    /// "pending" }</c>.
    /// </summary>
    /// <remarks>
    /// <b>Diagnostic only.</b> This is the one place where provider vocabulary is allowed through
    /// the seam, and it is allowed because ops need to see exactly which capability is holding an
    /// onboarding up. Both keys and values are opaque strings with no cross-provider meaning:
    /// display them, log them, alert on them — but never let control flow depend on them. Anything
    /// a consumer genuinely needs to act on belongs in a first-class field on this contract.
    /// Dictionary keys are written to the wire verbatim, without camel-casing.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Capabilities { get; init; }
}
