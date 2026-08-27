namespace Wayroo.Payments.Messages;

/// <summary>
/// How far a dispute has escalated through the card-network process, which determines whether money
/// has moved and what the merchant is being asked for.
/// </summary>
/// <remarks>
/// <para>
/// The stages are defined by the card networks rather than by any one payment provider, which is
/// what makes a closed set safe here. Providers name them differently — a notification of
/// chargeback, a retrieval request, a request for information are all the same stage — and the
/// producer maps them onto these values.
/// </para>
/// <para>
/// Adding a member is a breaking change — see the versioning policy on <see cref="PaymentEvents"/>.
/// </para>
/// </remarks>
public enum DisputeType
{
    /// <summary>
    /// The issuer is asking questions before deciding: a retrieval request, a request for
    /// information, or a notification that a chargeback is coming. No funds have moved yet, and a
    /// good response here can stop the dispute becoming a <see cref="Chargeback"/>.
    /// </summary>
    Inquiry = 0,

    /// <summary>
    /// A formal chargeback. Funds are debited — see
    /// <see cref="DisputeOpened.FundsDebited"/> — and the merchant must defend or accept before
    /// <see cref="DisputeOpened.DefenseDueBy"/>.
    /// </summary>
    Chargeback = 1,

    /// <summary>
    /// The issuer rejected the defense and escalated: a second chargeback, pre-arbitration, or
    /// arbitration. The remaining options are narrower and the deadlines shorter.
    /// </summary>
    SecondChargeback = 2,
}
