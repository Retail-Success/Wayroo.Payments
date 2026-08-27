using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Messages;

/// <summary>
/// Shared identity and the versioning policy for the provider-neutral payment integration events.
/// </summary>
/// <remarks>
/// <para>
/// These contracts describe money movement and merchant standing in terms the platform owns, so
/// that a consumer never has to know which payment provider produced the fact. The only provider
/// detail that crosses the seam is opaque: a <c>ProviderId</c> naming the provider and a set of
/// <c>Provider*Ref</c> strings that are meaningful only when handed back to that provider (for a
/// support lookup, a report join, or an API call). Nothing else provider-shaped belongs here — no
/// provider status enums, no provider error taxonomies, no provider account models.
/// </para>
/// <para>
/// <b>Versioning policy — additive-minor only.</b> Until the provider-decommission epic, every
/// change to these contracts must be backward compatible for consumers already deployed against an
/// earlier package version:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <b>Allowed:</b> adding a new <i>optional</i> (nullable) property. Consumers ignore unknown
///   properties by default (see <see cref="IntegrationJson"/>), so an older consumer keeps working.
///   </description></item>
///   <item><description>
///   <b>Allowed:</b> adding a whole new event type.
///   </description></item>
///   <item><description>
///   <b>Breaking — needs a new major:</b> adding, removing or renaming an <b>enum member</b>.
///   Enums travel as strings and an unrecognised string throws, so a value an older consumer has
///   never heard of turns every affected message into a poison message. This is why the enums
///   below are deliberately declared as complete sets up front, and why anything genuinely
///   open-ended (fee types, reason codes, capability names) is typed as a <c>string</c> instead.
///   </description></item>
///   <item><description>
///   <b>Breaking — needs a new major:</b> removing or renaming a property, changing its type, or
///   making an optional property required.
///   </description></item>
/// </list>
/// <para>
/// A major version is a new CLR type (<c>PayoutCompletedV2</c>) with <c>Version =&gt; 2</c>,
/// published in parallel with the old one until every consumer has moved. Because the major
/// version is part of the routed <c>detail-type</c> (<see cref="DetailType.Of{TEvent}"/>), a
/// consumer subscribed to <c>payments.PayoutCompleted.v1</c> never receives a <c>.v2</c> it did
/// not opt into.
/// </para>
/// </remarks>
public static class PaymentEvents
{
    /// <summary>
    /// The EventBridge <c>source</c> every payment event is published under: <c>"rs.payments"</c>.
    /// </summary>
    /// <remarks>
    /// The domain segment (<c>payments</c>) becomes the first segment of the routed
    /// <c>detail-type</c>, e.g. <c>payments.PayoutCompleted.v1</c>. Only the Wayroo.Payments
    /// service publishes under this source; other services consume.
    /// </remarks>
    public const string Source = "rs.payments";
}

/// <summary>
/// Well-known values for the opaque <c>ProviderId</c> carried by every payment event.
/// </summary>
/// <remarks>
/// <para>
/// The value is a lowercase, stable slug naming the payment provider the fact came from. It exists
/// so that mixed-fleet consumers — those handling stores on either provider during the migration —
/// can tag, filter and display per provider, and so that a <c>Provider*Ref</c> can be resolved
/// against the right system.
/// </para>
/// <para>
/// It is <b>not</b> a branch point for business logic. Consumers derive behaviour from the neutral
/// fields (status, amounts, timestamps); if a handler needs an <c>if (providerId == ...)</c> to
/// decide what a fact means, the contract is missing a neutral field and should gain one.
/// </para>
/// <para>
/// Declared as constants rather than an enum on purpose: the set of providers changes on a
/// business timescale, and adding an enum member would be a breaking change for every deployed
/// consumer under the policy on <see cref="PaymentEvents"/>.
/// </para>
/// </remarks>
public static class ProviderIds
{
    /// <summary>ProPay, the provider being migrated away from.</summary>
    public const string Propay = "propay";

    /// <summary>Adyen for Platforms, the provider being migrated to.</summary>
    public const string Adyen = "adyen";
}
