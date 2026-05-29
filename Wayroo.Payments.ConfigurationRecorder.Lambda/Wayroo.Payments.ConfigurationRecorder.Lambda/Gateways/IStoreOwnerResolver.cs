using Wayroo.Common.Exceptions;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways;

/// <summary>
/// Looks up which Wayroo store + tenant own a given provider account number. Each gateway provides its
/// own implementation under <c>Gateways/{Provider}/</c> because the upstream API differs per gateway
/// (the ProPay implementation here hits the Orders <c>/propay/accounts/{n}/store</c> endpoint).
/// </summary>
public interface IStoreOwnerResolver
{
    /// <summary>
    /// Resolves the Wayroo store + tenant that own the supplied provider account.
    /// </summary>
    /// <exception cref="ResourceAccessException">
    /// The account isn't currently resolvable (e.g. the store hasn't completed onboarding). Transient →
    /// the recorder lambda's <see cref="ProcessFailureHandler"/> re-queues the message.
    /// </exception>
    Task<StoreOwner> Resolve(long accountNumber, CancellationToken cancellationToken);
}

/// <summary>The Wayroo store + tenant that own a provider account.</summary>
public sealed record StoreOwner(long StoreId, long TenantId);
