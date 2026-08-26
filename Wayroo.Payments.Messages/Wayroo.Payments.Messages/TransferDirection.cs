namespace Wayroo.Payments.Messages;

/// <summary>
/// Which way the money was going when the transfer that came back was made.
/// </summary>
/// <remarks>
/// <para>
/// The distinction decides who the consumer has to tell and what it has to undo. An outbound return
/// is a payout that bounced — the merchant did not get paid and the funds are back on their
/// balance. An inbound return is money a payer sent that was later pulled back, which leaves an
/// order paid for with funds that no longer exist.
/// </para>
/// <para>
/// This is a closed set. Adding a member is a breaking change — see the versioning policy on
/// <see cref="PaymentEvents"/>.
/// </para>
/// </remarks>
public enum TransferDirection
{
    /// <summary>
    /// A payout to the merchant's bank was returned, typically for bad account details or a closed
    /// account. The funds are back on the merchant's balance and the bank details need fixing
    /// before the next payout will succeed.
    /// </summary>
    Outbound = 0,

    /// <summary>
    /// A funding transfer into the platform was returned — an ACH debit rejected by the payer's
    /// bank for insufficient funds, a closed account, or a revoked authorization. Whatever that
    /// money paid for is now unfunded.
    /// </summary>
    Inbound = 1,
}
