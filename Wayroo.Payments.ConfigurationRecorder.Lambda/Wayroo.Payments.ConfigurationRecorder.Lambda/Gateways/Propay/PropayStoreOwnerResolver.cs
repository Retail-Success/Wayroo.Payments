using Luci.Orders.SDK.Api;
using Wayroo.Common.Exceptions;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways.Propay;

/// <summary>
/// ProPay-specific <see cref="IStoreOwnerResolver"/>: looks up the Wayroo store + tenant that own a
/// ProPay account by calling the Orders API (<c>GET /propay/accounts/{accountNumber}/store</c>).
/// </summary>
public class PropayStoreOwnerResolver(IStorePropayClient storePropayClient) : IStoreOwnerResolver
{
    public async Task<StoreOwner> Resolve(long accountNumber, CancellationToken cancellationToken)
    {
        var owner = await storePropayClient.GetStoreIdFromPropayAccountNumberAsync(accountNumber, cancellationToken);
        if (owner is null || owner.StoreId <= 0)
        {
            // Transient — most likely the store hasn't finished onboarding yet.
            throw new ResourceAccessException(
                $"No store resolved for ProPay account {accountNumber}; re-queueing to retry.");
        }

        return new StoreOwner(StoreId: owner.StoreId, TenantId: owner.TenantId);
    }
}
