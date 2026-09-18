namespace Wayroo.Payments.BusinessLogic.Gateways.Adyen;

/// <summary>
/// What the gateway needs in order to create a seller's legal entity — who this person legally is.
/// </summary>
/// <remarks>
/// Carries the minimum Adyen accepts on a create, not the minimum it needs to verify anyone. The rest
/// of the identity — date of birth, address, and the identity document or Social Security number US
/// verification turns on — is collected from the seller by Adyen's own hosted onboarding, which is
/// the whole point of using it: the platform never holds it.
/// </remarks>
public sealed record AdyenLegalEntityRequest
{
    /// <summary>
    /// The tenant the store sells for. Needed only so the reference can name the store while the
    /// platform has no person identity to name instead.
    /// </summary>
    public required long TenantId { get; init; }

    /// <summary>The store being onboarded.</summary>
    public required long StoreId { get; init; }

    /// <summary>
    /// Our identifier for the person who owns the store, when one is known.
    /// </summary>
    /// <remarks>
    /// Absent today, because the platform holds no person identity — so the reference written on the
    /// legal entity falls back to the store. The seam is here so that the day a person identifier
    /// exists, a returning seller's legal entity is found and reused rather than duplicated.
    /// </remarks>
    public Guid? OwnerId { get; init; }

    /// <summary>The seller's first name.</summary>
    public required string FirstName { get; init; }

    /// <summary>The seller's last name.</summary>
    public required string LastName { get; init; }

    /// <summary>The two-letter country of the seller's residential address.</summary>
    public required string ResidentialCountry { get; init; }
}

/// <summary>
/// What the gateway needs in order to create a seller's business line — what they sell for this
/// tenant, and through which channels.
/// </summary>
/// <remarks>
/// Its own rung rather than a detail of the legal entity, because business-line verification is what
/// gates <see cref="Models.AdyenCapability.ReceivePayments"/>: without one the capability can never
/// become usable, and a seller who completed every identity check would still be unable to take a
/// payment.
/// </remarks>
public sealed record AdyenBusinessLineRequest
{
    /// <summary>The tenant the seller sells for. One business line exists per seller per tenant.</summary>
    public required long TenantId { get; init; }

    /// <summary>The store being onboarded.</summary>
    public required long StoreId { get; init; }

    /// <summary>The legal entity the business line belongs to, from the rung before.</summary>
    public required string LegalEntityId { get; init; }

    /// <summary>Adyen's code for the industry the seller trades in.</summary>
    /// <remarks>
    /// A property of the tenant rather than of the individual seller: everyone selling for one
    /// direct-selling tenant sells that tenant's goods.
    /// </remarks>
    public required string IndustryCode { get; init; }

    /// <summary>
    /// The channels the seller sells through, in Adyen's vocabulary — <c>pos</c>, <c>eCommerce</c>
    /// and the rest.
    /// </summary>
    /// <remarks>
    /// Card-present and card-not-present are verified separately: point of sale turns on identity and
    /// business-line checks, while selling online additionally requires the seller's website content
    /// to be verified. Point-of-sale only is a legitimate end state, so the channels a seller starts
    /// with are the caller's decision rather than a constant here.
    /// </remarks>
    public required IReadOnlyList<string> SalesChannels { get; init; }
}

/// <summary>
/// What the gateway needs in order to create a seller's account holder — what they are permitted to do.
/// </summary>
public sealed record AdyenAccountHolderRequest
{
    /// <summary>
    /// The tenant the store sells for. Needed only so the reference can name the store while the
    /// platform has no person identity to name instead.
    /// </summary>
    public required long TenantId { get; init; }

    /// <summary>The store being onboarded.</summary>
    public required long StoreId { get; init; }

    /// <summary>The person who owns the store, when known. See <see cref="AdyenLegalEntityRequest.OwnerId"/>.</summary>
    public Guid? OwnerId { get; init; }

    /// <summary>The legal entity the account holder acts for.</summary>
    public required string LegalEntityId { get; init; }

    /// <summary>
    /// Which attempt at onboarding this is, from <see cref="Models.AdyenAccount.OnboardingGeneration"/>.
    /// </summary>
    /// <remarks>
    /// Feeds the idempotency key. A retry of one attempt replays its account holder; a deliberate
    /// fresh attempt gets a new one.
    /// </remarks>
    public required long OnboardingGeneration { get; init; }
}

/// <summary>
/// What the gateway needs in order to create the balance account a store's earnings land in.
/// </summary>
public sealed record AdyenBalanceAccountRequest
{
    /// <summary>The tenant the earnings are for. One balance account exists per seller per tenant.</summary>
    public required long TenantId { get; init; }

    /// <summary>The store being onboarded.</summary>
    public required long StoreId { get; init; }

    /// <summary>The account holder the balance account belongs to, from the rung before.</summary>
    public required string AccountHolderId { get; init; }

    /// <summary>
    /// The currency the account holds, as a three-letter code.
    /// </summary>
    /// <remarks>
    /// <b>Always sent, never left to Adyen.</b> An omitted currency yields EUR even on a US platform,
    /// and a balance account's currency cannot be changed after it is created — so the store would
    /// have to be onboarded again to correct it.
    /// </remarks>
    public required string CurrencyCode { get; init; }

    /// <summary>Which attempt at onboarding this is. See <see cref="AdyenAccountHolderRequest.OnboardingGeneration"/>.</summary>
    public required long OnboardingGeneration { get; init; }
}

/// <summary>
/// What the gateway needs in order to mint a link to Adyen's hosted onboarding page.
/// </summary>
/// <remarks>
/// Not a rung of the ladder: the ladder builds the objects, and this is how the seller is then sent to
/// Adyen to complete verification themselves. Links are short-lived by design, so one is minted per
/// visit rather than stored.
/// </remarks>
public sealed record AdyenOnboardingLinkRequest
{
    /// <summary>The store the seller is onboarding.</summary>
    public required long StoreId { get; init; }

    /// <summary>The legal entity the seller is completing verification for.</summary>
    public required string LegalEntityId { get; init; }

    /// <summary>Where Adyen returns the seller when they finish, when a destination is configured.</summary>
    public string? RedirectUrl { get; init; }

    /// <summary>The language to present the page in, when one is configured.</summary>
    public string? Locale { get; init; }

    /// <summary>Which of the hosted-onboarding themes to render, when one is configured.</summary>
    public string? ThemeId { get; init; }
}
