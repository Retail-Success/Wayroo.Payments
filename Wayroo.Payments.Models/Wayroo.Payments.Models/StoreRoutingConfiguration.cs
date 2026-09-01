namespace Wayroo.Payments.Models;

/// <summary>
/// Which payment provider a store transacts through, and where it sits in the migration between
/// providers. One record per store — this service is the source of truth for the fact, and Orders
/// keeps a read model of it.
/// </summary>
/// <remarks>
/// <para>
/// Stored alongside the store's per-provider configurations but under its own reserved sort key,
/// because it is a fact about the <i>store</i> rather than about any one provider. Keeping it in one
/// place is what lets it be read in a single call and versioned as a unit.
/// </para>
/// <para>
/// A store with no routing record transacts through the platform's default provider. That is what
/// makes the whole mechanism inert until something deliberately writes a record: nothing has to be
/// backfilled for existing stores to keep behaving exactly as they did.
/// </para>
/// </remarks>
public class StoreRoutingConfiguration
{
    /// <summary>The store this routing applies to.</summary>
    public long StoreId { get; set; }

    /// <summary>The tenant the store belongs to.</summary>
    public long? TenantId { get; set; }

    /// <summary>
    /// The provider new payments are routed to, read only at payment-session creation. See
    /// <see cref="ProviderIds"/>; treat as opaque.
    /// </summary>
    /// <remarks>
    /// Only <i>new</i> money follows this. Money already taken keeps routing by its original
    /// transaction, so a store that has moved provider still refunds and disputes through the old one.
    /// </remarks>
    public string AcquiringProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Where the store sits in the provider migration, for operations. See <see cref="MigrationStates"/>.
    /// </summary>
    /// <remarks>
    /// A <see cref="string"/> rather than an enum, deliberately: this vocabulary moves as the migration
    /// runs, and the value travels on an event contract where adding an enum member would break every
    /// consumer on an older package version.
    /// </remarks>
    public string MigrationState { get; set; } = MigrationStates.PropayActive;

    /// <summary>
    /// Increments on every write. Travels as the integration event's <c>Sequence</c> so a consumer can
    /// drop an update older than the one it has already applied — necessary because delivery is
    /// at-least-once and unordered.
    /// </summary>
    public long ConfigurationVersion { get; set; }

    /// <summary>When the routing record was first written.</summary>
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>When the routing record was last written.</summary>
    public DateTimeOffset? ModifiedOn { get; set; }
}

/// <summary>
/// The well-known values of <see cref="StoreRoutingConfiguration.MigrationState"/>.
/// </summary>
/// <remarks>
/// Constants rather than an enum, for the reason given on
/// <see cref="StoreRoutingConfiguration.MigrationState"/>. Treat an unrecognised value as "somewhere in
/// the migration" and fall back on <see cref="StoreRoutingConfiguration.AcquiringProviderId"/>, which is
/// the field that actually decides anything.
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
