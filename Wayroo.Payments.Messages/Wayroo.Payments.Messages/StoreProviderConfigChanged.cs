using System.Text.Json.Serialization;
using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Messages;

/// <summary>
/// A store's payment-provider configuration changed — which provider it acquires through, or where it
/// sits in the migration between providers.
/// </summary>
/// <remarks>
/// <para>
/// Wayroo.Payments owns this fact; consumers keep read models of it. Orders is the first, using it to
/// decide which payment form a store gets at session creation. Only <i>new</i> money follows this
/// event: money already taken routes by its original transaction, so a store that has moved provider
/// still refunds and disputes through the one it was on at the time.
/// </para>
/// <para>
/// The event is a <i>statement of current configuration</i>, not a delta, so a consumer that missed
/// earlier events still converges on the right state from the latest one. Delivery is at-least-once
/// and unordered: deduplicate on <see cref="IntegrationMetadata.EventId"/> and drop anything whose
/// <see cref="IntegrationMetadata.Sequence"/> has already been applied. The sequence carries the
/// store's configuration version, incremented server-side on every write, so it is a reliable
/// staleness test rather than a timestamp comparison.
/// </para>
/// <para>
/// A store with no read-model row has never been configured away from the platform default, which is
/// why a consumer can treat an absent row as the default provider and deploy inert.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var message = IntegrationJson.DeserializeMessage&lt;StoreProviderConfigChanged&gt;(sqsMessage.Body);
/// StoreProviderConfigChanged config = message.Detail.Data;
///
/// await _storeProviders.ApplyAsync(
///     message.Detail.Metadata.Scope.RequireStoreId(),
///     config.AcquiringProviderId,
///     version: message.Detail.Metadata.Sequence,
///     ct);
/// </code>
/// </example>
public sealed record StoreProviderConfigChanged : IStoreScopedEvent
{
    /// <inheritdoc cref="PaymentEvents.Source"/>
    public static string Source => PaymentEvents.Source;

    /// <summary>The bare event name: <c>"StoreProviderConfigChanged"</c>.</summary>
    public static string EventName => nameof(StoreProviderConfigChanged);

    /// <summary>Major version 1. Routed as <c>payments.StoreProviderConfigChanged.v1</c>.</summary>
    public static int Version => 1;

    /// <summary>
    /// The provider the store acquires through as of this event. See <see cref="ProviderIds"/> — treat
    /// as opaque, and route on it rather than inferring from anything else here.
    /// </summary>
    public required string AcquiringProviderId { get; init; }

    /// <summary>
    /// Where the store sits in the provider migration. See <see cref="MigrationStates"/>.
    /// </summary>
    /// <remarks>
    /// Operational context, not a routing input — <see cref="AcquiringProviderId"/> is what decides
    /// where money goes. A <see cref="string"/> rather than an enum on purpose: this vocabulary moves
    /// as the migration runs, and an enum member a consumer on an older package has never heard of
    /// would turn every affected message into a poison message. Tolerate values you do not recognise.
    /// </remarks>
    public required string MigrationState { get; init; }

    /// <summary>
    /// The provider the store acquired through immediately before, when the producer knows it. Present
    /// so a consumer can react to a transition rather than to a level. Null on the first event for a
    /// store — never read a null as "unchanged".
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PreviousAcquiringProviderId { get; init; }
}

/// <summary>
/// The well-known values of <see cref="StoreProviderConfigChanged.MigrationState"/>.
/// </summary>
/// <remarks>
/// Constants rather than an enum, for the reason given on
/// <see cref="StoreProviderConfigChanged.MigrationState"/>. Mirrors
/// <c>Wayroo.Payments.Models.MigrationStates</c>; the two are held together by
/// <c>MigrationStateParityTests</c>.
/// </remarks>
public static class MigrationStates
{
    /// <summary>On ProPay, not yet invited to migrate. The state every store starts in.</summary>
    public const string PropayActive = "PropayActive";

    /// <summary>Invited to onboard with the new provider; nothing has changed for the merchant yet.</summary>
    public const string AdyenOnboardingInvited = "AdyenOnboardingInvited";

    /// <summary>Onboarding submitted; the provider is verifying the merchant.</summary>
    public const string AdyenKycInProgress = "AdyenKycInProgress";

    /// <summary>Verified and able to process, but not yet switched over.</summary>
    public const string AdyenReady = "AdyenReady";

    /// <summary>Switched: new money goes to the new provider while the old one still carries its tail.</summary>
    public const string AdyenActivePropayTail = "AdyenActive(PropayTail)";

    /// <summary>Switched, and the old provider's account is closed.</summary>
    public const string AdyenActivePropayClosed = "AdyenActive(PropayClosed)";
}
