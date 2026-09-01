namespace Wayroo.Payments.Models;

/// <summary>
/// The outcome of writing a store's routing: what it now is, and what it was before.
/// </summary>
/// <remarks>
/// Both halves are returned because the decision that follows a routing write — whether to announce it
/// — depends on whether anything actually moved. Reading the record separately to find out would race
/// with other writers and could announce a change that never happened, or miss one that did.
/// </remarks>
/// <param name="Current">The routing as it now stands, including the newly assigned version.</param>
/// <param name="Previous">
/// The routing as it was, or <c>null</c> when this write created the record — which is itself a change
/// worth announcing.
/// </param>
public sealed record StoreRoutingWriteResult(
    StoreRoutingConfiguration Current,
    StoreRoutingConfiguration? Previous)
{
    /// <summary>
    /// Whether this write moved the routing fact, as opposed to rewriting the same values.
    /// </summary>
    public bool Changed =>
        Previous is null
        || !string.Equals(Previous.AcquiringProviderId, Current.AcquiringProviderId, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(Previous.MigrationState, Current.MigrationState, StringComparison.Ordinal);
}
