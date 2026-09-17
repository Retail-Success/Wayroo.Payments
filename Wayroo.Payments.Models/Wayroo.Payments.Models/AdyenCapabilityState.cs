namespace Wayroo.Payments.Models;

/// <summary>
/// Where one capability stands, as Adyen last reported it.
/// </summary>
/// <remarks>
/// The three flags are independent, not a progression: a freshly created account holder comes back
/// <c>Requested</c>, <c>Enabled</c> and <b>not</b> <c>Allowed</c> at the same time. Only
/// <see cref="Allowed"/> together with a valid <see cref="VerificationStatus"/> means the capability
/// can actually be used.
/// </remarks>
public class AdyenCapabilityState
{
    /// <summary>Whether we asked for this capability.</summary>
    public bool Requested { get; set; }

    /// <summary>Whether the capability is switched on at Adyen.</summary>
    /// <remarks>On without being <see cref="Allowed"/> is an ordinary state, not a contradiction.</remarks>
    public bool Enabled { get; set; }

    /// <summary>Whether Adyen currently permits the capability to be used.</summary>
    /// <remarks>This is the flag a decision to route money keys on.</remarks>
    public bool Allowed { get; set; }

    /// <summary>Adyen's verification status, verbatim: <c>pending</c>, <c>valid</c>, <c>invalid</c> or <c>rejected</c>.</summary>
    /// <remarks>
    /// Kept as text rather than an enum because it arrives on webhooks: a value we have never seen
    /// should be recorded and treated as not-valid, never throw in a consumer.
    /// </remarks>
    public string? VerificationStatus { get; set; }

    /// <summary>
    /// When a capability that is allowed but not valid stops being allowed.
    /// </summary>
    /// <remarks>
    /// Adyen grants a grace period after verification fails: the seller keeps trading while they sort
    /// the problem out, until this deadline. Absent when there is no grace period running, which is
    /// what lets an index find only the accounts that have one.
    /// </remarks>
    public DateTimeOffset? GraceUntil { get; set; }

    /// <summary>
    /// The sequence of the last event applied to this capability.
    /// </summary>
    /// <remarks>
    /// Capability updates arrive at least once and out of order, so an update carrying a sequence at
    /// or below this one is stale and must be discarded rather than applied.
    /// </remarks>
    public long? LastEventSequence { get; set; }

    /// <summary>
    /// Whether this capability is usable right now: permitted, and verified.
    /// </summary>
    public bool IsUsable =>
        Allowed && string.Equals(VerificationStatus, "valid", StringComparison.OrdinalIgnoreCase);
}
