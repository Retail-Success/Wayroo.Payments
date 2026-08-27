namespace Wayroo.Payments.Messages;

/// <summary>
/// The provider-neutral standing of a store's merchant account: what the platform is allowed to do
/// with it, and what, if anything, the merchant has to do next.
/// </summary>
/// <remarks>
/// <para>
/// Every provider models onboarding and account health differently — ProPay exposes two dozen
/// account statuses, Adyen exposes per-capability <c>allowed</c> flags crossed with a verification
/// status. Producers collapse that detail into these values; consumers branch on them and on
/// <see cref="MerchantAccountStatusChanged.CanProcessPayments"/>, never on the provider's own
/// vocabulary.
/// </para>
/// <para>
/// The status answers "where is this account in its lifecycle"; it does <b>not</b> by itself answer
/// "can this store take money right now". Those come apart during a grace period, where an account
/// is <see cref="ActionRequired"/> but still processing until a deadline — which is why
/// <see cref="MerchantAccountStatusChanged.CanProcessPayments"/> is a separate, explicit field
/// rather than something a consumer infers from the status.
/// </para>
/// <para>
/// This is a closed set. Adding a member is a breaking change — see the versioning policy on
/// <see cref="PaymentEvents"/>.
/// </para>
/// </remarks>
public enum MerchantAccountStatus
{
    /// <summary>
    /// Onboarding or verification is under way and nothing is required of the merchant right now.
    /// The account cannot process payments yet.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The provider needs something from the merchant — a document, a correction, an identity
    /// check. Whether the store can keep processing meanwhile is carried by
    /// <see cref="MerchantAccountStatusChanged.CanProcessPayments"/>, and the deadline, when there
    /// is one, by <see cref="MerchantAccountStatusChanged.ActionRequiredBy"/>.
    /// </summary>
    ActionRequired = 1,

    /// <summary>
    /// Fully verified and enabled. This is the state that enables a store for selling and triggers
    /// the downstream enablement work.
    /// </summary>
    ReadyToProcess = 2,

    /// <summary>
    /// Processing is blocked by the provider or the platform — a risk hold, an investigation, an
    /// administrative pause. Recoverable: an account can return to <see cref="ReadyToProcess"/>.
    /// </summary>
    Suspended = 3,

    /// <summary>
    /// Verification failed terminally. The account will not become usable; onboarding has to start
    /// over with a new account. Do not schedule retries against this state.
    /// </summary>
    Rejected = 4,

    /// <summary>
    /// The account is closed, by the merchant, the platform or the provider. Terminal, and distinct
    /// from <see cref="Rejected"/>: a closed account may have been perfectly healthy, and may still
    /// have a financial tail (disputes, returns) that produces further events.
    /// </summary>
    Closed = 5,
}
