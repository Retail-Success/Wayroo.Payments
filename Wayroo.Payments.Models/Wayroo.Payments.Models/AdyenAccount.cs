namespace Wayroo.Payments.Models;

/// <summary>
/// What this service knows about a store's Adyen account: the identifiers it created, how far
/// onboarding got, and where each capability stands.
/// </summary>
/// <remarks>
/// <para>
/// Stored on the store's <c>adyen</c> provider record, alongside the ProPay record in the same
/// partition. The attributes here are written by the onboarding ladder and by capability webhooks,
/// and by nothing else — the recorder's credential payload and the account-refresh attributes sit on
/// the same record untouched, which is why every writer names only what it owns.
/// </para>
/// <para>
/// <b>Two of these identifiers belong to a person, not to this store.</b>
/// <see cref="LegalEntityId"/> and <see cref="AccountHolderId"/> are the seller themselves, so a
/// seller who sells for more than one tenant carries the same values on each of their store
/// records — reusing one verified legal entity is what spares them from repeating verification.
/// <see cref="BusinessLineId"/> and <see cref="BalanceAccountId"/> genuinely belong to this store, so
/// that earnings for different tenants never commingle.
/// </para>
/// </remarks>
public class AdyenAccount
{
    /// <summary>The store this record belongs to.</summary>
    public long StoreId { get; set; }

    /// <summary>The tenant the store belongs to.</summary>
    public long? TenantId { get; set; }

    /// <summary>
    /// Our identifier for the person who owns the store, used to find a legal entity they already have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Adyen offers no way to search for a legal entity by anything we set — not by reference, and
    /// certainly not by identity document — so recognising a returning seller depends entirely on this
    /// being recorded here.
    /// </para>
    /// <para>
    /// Ours, not the identity provider's, even though it is seeded from it: this value is written into
    /// references that outlive any auth migration, and one pointing at a vendor's identifier becomes
    /// an orphan the moment that vendor is gone.
    /// </para>
    /// </remarks>
    public Guid? OwnerId { get; set; }

    /// <summary>The seller's legal entity. Belongs to the person, not to this store.</summary>
    public string? LegalEntityId { get; set; }

    /// <summary>The seller's business line for this store's tenant.</summary>
    public string? BusinessLineId { get; set; }

    /// <summary>The seller's account holder, which carries the capabilities. Belongs to the person.</summary>
    public string? AccountHolderId { get; set; }

    /// <summary>The balance account this store's earnings land in.</summary>
    public string? BalanceAccountId { get; set; }

    /// <summary>How far onboarding has got, and where a re-run resumes.</summary>
    public AdyenOnboardingStep OnboardingStep { get; set; }

    /// <summary>
    /// Which attempt at onboarding this record is on. Starts at one and only ever moves forward.
    /// </summary>
    /// <remarks>
    /// Part of the idempotency key sent to Adyen, and the reason a deliberate re-onboarding creates a
    /// new account holder instead of replaying the old one: without it, the key for a store's second
    /// attempt would be identical to its first, and Adyen would answer with the account holder the
    /// store is being moved away from.
    /// </remarks>
    public long OnboardingGeneration { get; set; } = 1;

    /// <summary>Where each capability stands, keyed by capability.</summary>
    public Dictionary<AdyenCapability, AdyenCapabilityState> Capabilities { get; init; } = [];

    /// <summary>The account's standing, derived from the capabilities.</summary>
    /// <remarks>
    /// Recorded rather than computed on read so that a single write returns both the previous and the
    /// new value, which is what lets a status change be published as a transition rather than a level.
    /// </remarks>
    public PaymentAccountStatus? AccountStatus { get; set; }

    /// <summary>
    /// A version incremented on every write to this record.
    /// </summary>
    /// <remarks>
    /// Travels as the sequence on the events published from it, so a consumer can discard an update
    /// older than one it has already applied. Incremented by the database rather than written from a
    /// value read earlier, so two concurrent writers cannot land on the same version.
    /// </remarks>
    public long AggregateVersion { get; set; }

    /// <summary>When the record was first written.</summary>
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>When the record was last written.</summary>
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>
    /// Whether this store may be routed real money: every capability a payment depends on is permitted
    /// and verified.
    /// </summary>
    public bool CanProcessPayments =>
        AdyenCapabilities.RequiredToProcessPayments.All(
            capability => Capabilities.TryGetValue(capability, out var state) && state.IsUsable);

    /// <summary>
    /// The soonest grace period to expire, or <c>null</c> when none is running.
    /// </summary>
    /// <remarks>
    /// What an operator chasing a seller before their deadline needs, and the reason a grace deadline
    /// is left absent rather than set to a sentinel when there is no grace period.
    /// </remarks>
    public DateTimeOffset? NextGraceDeadline =>
        Capabilities.Values
            .Select(state => state.GraceUntil)
            .Where(deadline => deadline.HasValue)
            .OrderBy(deadline => deadline!.Value)
            .FirstOrDefault();
}
