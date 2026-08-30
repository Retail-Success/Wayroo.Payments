using System.Text.Json.Serialization;

namespace Wayroo.Payments.Models;

/// <summary>
/// The provider-neutral standing of a store's merchant account, as returned by the payment account
/// endpoints. Answers "where is this account in its lifecycle"; it does <b>not</b> by itself answer
/// "can this store take money right now" — that is
/// <see cref="PaymentAccountBalance.CanProcessPayments"/>.
/// </summary>
/// <remarks>
/// <para>
/// This mirrors <c>Wayroo.Payments.Messages.MerchantAccountStatus</c> member for member, deliberately
/// as a separate type: <c>Wayroo.Payments.Models</c> carries no package references, so the API read
/// models stay free of the event package and its additive-minor versioning policy. Converting between
/// the two is a name-for-name mapping.
/// </para>
/// <para>
/// <b>Keep the two in sync.</b> <c>PaymentAccountStatusParityTests</c> in
/// <c>Wayroo.Payments.API.UnitTests</c> fails the build if a member is added to one and not the other.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentAccountStatus
{
    /// <summary>
    /// Onboarding or verification is under way and nothing is required of the merchant right now.
    /// The account cannot process payments yet.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The provider needs something from the merchant — a document, a correction, an identity check.
    /// </summary>
    ActionRequired = 1,

    /// <summary>Fully verified and enabled. The state in which a store can sell.</summary>
    ReadyToProcess = 2,

    /// <summary>
    /// Processing is blocked by the provider or the platform — a risk hold, an investigation, an
    /// administrative pause. Recoverable: an account can return to <see cref="ReadyToProcess"/>.
    /// </summary>
    Suspended = 3,

    /// <summary>
    /// Verification failed terminally. The account will not become usable; onboarding has to start
    /// over with a new account.
    /// </summary>
    Rejected = 4,

    /// <summary>
    /// The account is closed, by the merchant, the platform or the provider. Terminal, and distinct
    /// from <see cref="Rejected"/>.
    /// </summary>
    Closed = 5,
}
