namespace Wayroo.Payments.Messages;

/// <summary>
/// Where a money-movement fact was learned from. Two sources describe the same money at different
/// times and with different authority, and reconciliation depends on telling them apart.
/// </summary>
/// <remarks>
/// <para>
/// This is a closed set. Adding a member is a breaking change — see the versioning policy on
/// <see cref="PaymentEvents"/>.
/// </para>
/// </remarks>
public enum RecordOrigin
{
    /// <summary>
    /// Derived from a provider webhook: fast, but at-least-once and occasionally incomplete.
    /// Good enough to update a read model; not the number to reconcile against.
    /// </summary>
    Webhook = 0,

    /// <summary>
    /// Derived from a provider's generated settlement or payout report. Slower — typically the next
    /// day — but complete, which makes it the authoritative figure and the gap-fill source when a
    /// webhook was missed.
    /// </summary>
    Report = 1,

    /// <summary>
    /// Entered by an operator to correct or backfill a fact the automated pipeline could not
    /// produce. Expect these to be rare and to warrant an audit trail on the consuming side.
    /// </summary>
    Manual = 2,
}
