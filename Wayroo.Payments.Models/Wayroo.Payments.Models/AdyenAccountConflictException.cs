namespace Wayroo.Payments.Models;

/// <summary>
/// Thrown when a write would overwrite an Adyen identifier already recorded for a store.
/// </summary>
/// <remarks>
/// Not a failure so much as a race resolved: two onboardings of one store ran at once, and this one
/// lost. The identifier already stored is the one in use — re-read the record and continue from it
/// rather than retrying the write.
/// </remarks>
public class AdyenAccountConflictException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="storeId">The store whose record was being written.</param>
    /// <param name="attributeName">The identifier that was already present.</param>
    public AdyenAccountConflictException(long storeId, string attributeName)
        : base($"Store {storeId} already has {attributeName} recorded; the stored value is the one in use.")
    {
        StoreId = storeId;
        AttributeName = attributeName;
    }

    /// <summary>The store whose record was being written.</summary>
    public long StoreId { get; }

    /// <summary>The identifier that was already present.</summary>
    public string AttributeName { get; }
}
