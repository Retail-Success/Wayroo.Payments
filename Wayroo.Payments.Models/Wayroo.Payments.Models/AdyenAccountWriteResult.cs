namespace Wayroo.Payments.Models;

/// <summary>
/// A write to a store's Adyen account record: what it held before, and what it holds now.
/// </summary>
/// <remarks>
/// Both halves come from the one write. Reading the previous state separately would need a second
/// call with a window between the two, and in that window another writer could land — which for a
/// status change would mean publishing a transition that never happened.
/// </remarks>
/// <param name="Current">The record as it now stands.</param>
/// <param name="Previous">The record as it stood before this write, or <c>null</c> if it is new.</param>
public sealed record AdyenAccountWriteResult(AdyenAccount Current, AdyenAccount? Previous)
{
    /// <summary>
    /// Whether the account's standing actually moved, as opposed to being rewritten unchanged.
    /// </summary>
    /// <remarks>
    /// Capability updates arrive at least once, so the same state is delivered more than once as a
    /// matter of course. This is what separates a real change worth announcing from a repeat.
    /// </remarks>
    public bool StatusChanged => Previous?.AccountStatus != Current.AccountStatus;
}
