namespace Wayroo.Payments.Models;

/// <summary>
/// What the platform tells Adyen about a seller when it opens their accounts.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the smallest set Adyen will accept, and deliberately not enough to verify anyone.
/// Everything verification actually turns on — date of birth, residential address, and for a US
/// individual a Social Security number — the seller gives to Adyen themselves, on Adyen's own
/// onboarding pages. <b>That the platform never holds those details is the reason onboarding works
/// this way</b>, so this contract growing is a decision, not a detail.
/// </para>
/// <para>
/// The name still has to be right: it is what Adyen matches against the identity the seller later
/// proves, and a mismatch is a verification failure the seller has to be walked through.
/// </para>
/// </remarks>
public class AdyenSellerDetails
{
    /// <summary>
    /// The platform's identifier for the person who owns the store, when one is known.
    /// </summary>
    /// <remarks>
    /// Omitted today, because the platform holds no person identity — no table, and not Frontegg's
    /// user either, which mints a new user per signup. Onboarding then creates a legal entity per
    /// store. Supplying it is what will one day let a seller who already verified with Adyen for one
    /// tenant start selling for another without verifying again.
    /// </remarks>
    public Guid? OwnerId { get; set; }

    /// <summary>The seller's first name, as it appears on their identity documents.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>The seller's last name, as it appears on their identity documents.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// The two-letter country code of where the seller lives.
    /// </summary>
    /// <remarks>
    /// Which country decides what Adyen will ask the seller for, so it is the one piece of address
    /// the platform does send.
    /// </remarks>
    public string ResidentialCountry { get; set; } = string.Empty;
}
