namespace Wayroo.Payments.Models;

/// <summary>
/// What an Adyen reference is anchored to.
/// </summary>
/// <remarks>
/// The balance platform is flat — a tenant's account holder and a seller's account holder are siblings,
/// and nothing in Adyen records that one belongs to the other. The hierarchy lives in our data model,
/// and the scope is how a reference expresses which level of it an object sits at.
/// </remarks>
public enum AdyenReferenceScope
{
    /// <summary>Retail Success itself — the liable balance account and anything else platform-wide.</summary>
    Platform = 0,

    /// <summary>A tenant — a direct selling organization such as Paparazzi or Jordan Essentials: its legal entity, account holder, merchant account, split profile.</summary>
    Tenant,

    /// <summary>
    /// A person, independent of any tenant they sell for — their legal entity and account holder.
    /// </summary>
    /// <remarks>
    /// Reusing one verified legal entity is what saves a seller from repeating KYC for every tenant they
    /// join, so this scope deliberately carries no tenant.
    /// </remarks>
    Person,
}
