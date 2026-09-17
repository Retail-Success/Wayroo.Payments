namespace Wayroo.Payments.Models;

/// <summary>
/// How far a store has got through Adyen onboarding.
/// </summary>
/// <remarks>
/// <para>
/// The resume cursor. Onboarding walks a fixed ladder of calls to Adyen, each depending on the one
/// before, and nothing spans them transactionally — so the step reached is recorded as each identifier
/// comes back rather than once at the end, and a re-run picks up from here instead of starting over.
/// </para>
/// <para>
/// There is no rollback to recover with: Adyen offers no way to delete a legal entity, so a half-built
/// account is completed forward, never undone.
/// </para>
/// <para>
/// Stored by <i>name</i> rather than number, so inserting a step can never silently reinterpret rows
/// already written.
/// </para>
/// </remarks>
public enum AdyenOnboardingStep
{
    /// <summary>Nothing has been created at Adyen for this store.</summary>
    NotStarted = 0,

    /// <summary>The legal entity exists — who this seller legally is.</summary>
    LegalEntityCreated,

    /// <summary>The business line exists — what they sell for this DSO, and through which channels.</summary>
    BusinessLineCreated,

    /// <summary>The account holder exists, with its capabilities requested.</summary>
    AccountHolderCreated,

    /// <summary>
    /// The balance account exists. Every object this service creates is in place; verification is a
    /// separate matter, driven by the merchant and reported through capability webhooks.
    /// </summary>
    Complete,
}
