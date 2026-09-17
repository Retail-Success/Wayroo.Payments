namespace Wayroo.Payments.Models;

/// <summary>
/// An Adyen capability — one thing an account holder is, or is not, permitted to do.
/// </summary>
/// <remarks>
/// <para>
/// There is no single "can this account trade" switch: holding and moving a balance is expressed as
/// several independent capabilities, each verified separately. A store is only safe to route real
/// money to once <see cref="ReceivePayments"/>, <see cref="ReceiveFromPlatformPayments"/> and
/// <see cref="SendToTransferInstrument"/> are all permitted and valid.
/// </para>
/// <para>
/// <see cref="ReceiveFromPlatformPayments"/> is the one that costs money when it is missing: without
/// it a split books to the platform's liable account instead of the seller's balance account, and
/// nothing errors — it surfaces at reconciliation.
/// </para>
/// <para>
/// The names are Adyen's own and travel in requests and webhooks, so they are matched as written.
/// </para>
/// </remarks>
public enum AdyenCapability
{
    /// <summary>Take card payments. Gated on identity and business-line verification.</summary>
    ReceivePayments = 0,

    /// <summary>Receive split funds from a platform payment into a balance account.</summary>
    ReceiveFromPlatformPayments,

    /// <summary>Pay out to a bank account. Gated on bank-account verification.</summary>
    SendToTransferInstrument,

    /// <summary>Move funds to another balance account — commission true-ups and clawbacks.</summary>
    SendToBalanceAccount,

    /// <summary>Receive funds from another balance account.</summary>
    ReceiveFromBalanceAccount,
}

/// <summary>
/// Groupings of <see cref="AdyenCapability"/> that carry a decision.
/// </summary>
public static class AdyenCapabilities
{
    /// <summary>Every capability requested when an account holder is created.</summary>
    public static readonly IReadOnlyList<AdyenCapability> All =
    [
        AdyenCapability.ReceivePayments,
        AdyenCapability.ReceiveFromPlatformPayments,
        AdyenCapability.SendToTransferInstrument,
        AdyenCapability.SendToBalanceAccount,
        AdyenCapability.ReceiveFromBalanceAccount,
    ];

    /// <summary>
    /// The three that must be permitted and valid before a store may be routed real money.
    /// </summary>
    /// <remarks>
    /// The other two cover internal movement between balance accounts, which no sale depends on.
    /// </remarks>
    public static readonly IReadOnlyList<AdyenCapability> RequiredToProcessPayments =
    [
        AdyenCapability.ReceivePayments,
        AdyenCapability.ReceiveFromPlatformPayments,
        AdyenCapability.SendToTransferInstrument,
    ];

    /// <summary>Adyen's own name for a capability, as it appears in requests and webhooks.</summary>
    public static string ToAdyenName(this AdyenCapability capability) => capability switch
    {
        AdyenCapability.ReceivePayments => "receivePayments",
        AdyenCapability.ReceiveFromPlatformPayments => "receiveFromPlatformPayments",
        AdyenCapability.SendToTransferInstrument => "sendToTransferInstrument",
        AdyenCapability.SendToBalanceAccount => "sendToBalanceAccount",
        AdyenCapability.ReceiveFromBalanceAccount => "receiveFromBalanceAccount",
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown Adyen capability."),
    };

    /// <summary>
    /// Reads Adyen's name for a capability.
    /// </summary>
    /// <remarks>
    /// A name we do not recognise reads as <c>null</c> rather than throwing: Adyen validates against a
    /// far larger set than the five requested here, and an unfamiliar one arriving on a webhook is
    /// something to record and ignore, not something to fail on.
    /// </remarks>
    public static AdyenCapability? FromAdyenName(string? name) => name switch
    {
        "receivePayments" => AdyenCapability.ReceivePayments,
        "receiveFromPlatformPayments" => AdyenCapability.ReceiveFromPlatformPayments,
        "sendToTransferInstrument" => AdyenCapability.SendToTransferInstrument,
        "sendToBalanceAccount" => AdyenCapability.SendToBalanceAccount,
        "receiveFromBalanceAccount" => AdyenCapability.ReceiveFromBalanceAccount,
        _ => null,
    };
}
