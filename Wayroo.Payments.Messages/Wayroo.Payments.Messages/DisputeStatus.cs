namespace Wayroo.Payments.Messages;

/// <summary>
/// Where a dispute stands: whose move it is, and whether the money has been settled either way.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Won"/>, <see cref="Lost"/>, <see cref="Accepted"/>, <see cref="Expired"/> and
/// <see cref="Withdrawn"/> are terminal for a given dispute stage — but a lost dispute can be
/// re-opened one stage further on as a <see cref="DisputeType.SecondChargeback"/>, which arrives as
/// a new <see cref="DisputeOpened"/> rather than as a status change on this one.
/// </para>
/// <para>
/// This is a closed set. Adding a member is a breaking change — see the versioning policy on
/// <see cref="PaymentEvents"/>.
/// </para>
/// </remarks>
public enum DisputeStatus
{
    /// <summary>
    /// Open and awaiting a decision from the platform or the merchant, until
    /// <see cref="DisputeStatusChanged.DefenseDueBy"/>.
    /// </summary>
    Open = 0,

    /// <summary>
    /// Evidence has been submitted and the issuer is reviewing it. Nothing more to do but wait for
    /// <see cref="Won"/> or <see cref="Lost"/>.
    /// </summary>
    Defended = 1,

    /// <summary>
    /// Liability was accepted without contesting it — a deliberate choice not to spend the effort,
    /// not a failure. The debit stands.
    /// </summary>
    Accepted = 2,

    /// <summary>Resolved in the merchant's favour; disputed funds are returned.</summary>
    Won = 3,

    /// <summary>Resolved against the merchant; the debit stands.</summary>
    Lost = 4,

    /// <summary>
    /// The window to respond closed with no defense submitted. Financially the same as
    /// <see cref="Lost"/>, but distinct because it means a deadline was missed rather than a case
    /// argued — worth alerting on.
    /// </summary>
    Expired = 5,

    /// <summary>
    /// The issuer or cardholder withdrew the dispute before it was decided; any debit is reversed.
    /// </summary>
    Withdrawn = 6,
}
