using System.Net;
using Luci.Orders.SDK.Api;
using Luci.Orders.SDK.Models.StorePropay;
using Refit;
using Wayroo.Common.Exceptions;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways.Propay;

/// <summary>
/// ProPay-specific <see cref="IStoreOwnerResolver"/>: looks up the Wayroo store + tenant that own a
/// ProPay account by calling the Orders API (<c>GET /propay/accounts/{accountNumber}/store</c>).
/// An unresolved store and most unexpected Orders SDK failures (server-side, throttling, timeouts,
/// transport errors) are surfaced as <see cref="ResourceAccessException"/> so the message is treated
/// as transient and re-queued. A 4xx client error (other than 408/429) is left to propagate so the
/// message is dead-lettered, because retrying a malformed/unauthorized/not-found request won't help.
/// </summary>
public class PropayStoreOwnerResolver(IStorePropayClient storePropayClient) : IStoreOwnerResolver
{
    public async Task<StoreOwner> Resolve(long accountNumber, CancellationToken cancellationToken)
    {
        GetStoreIdFromPropayAccountNumberResponse? owner;
        try
        {
            owner = await storePropayClient.GetStoreIdFromPropayAccountNumberAsync(accountNumber, cancellationToken);
        }
        catch (Exception failure) when (ShouldReQueue(failure))
        {
            // Treat the failure as transient: surfacing it as a ResourceAccessException re-queues the
            // message to retry (ProcessFailureHandler logs the original exception as a warning), rather
            // than dead-lettering on a blip we can't classify. Permanent 4xx client errors fall through
            // this filter and propagate as-is so they are dead-lettered.
            throw new ResourceAccessException(
                $"Orders lookup failed for ProPay account {accountNumber}; treating as transient and re-queueing to retry.",
                failure);
        }

        if (owner is null || owner.StoreId <= 0)
        {
            // Transient — most likely the store hasn't finished onboarding yet.
            throw new ResourceAccessException(
                $"No store resolved for ProPay account {accountNumber}; re-queueing to retry.");
        }

        return new StoreOwner(StoreId: owner.StoreId, TenantId: owner.TenantId);
    }

    private static bool ShouldReQueue(Exception failure)
    {
        // Already a transient signal (e.g. store not yet onboarded). Leave it unchanged — re-wrapping
        // would only nest the same exception type, and ProcessFailureHandler already re-queues it.
        if (failure is ResourceAccessException)
            return false;

        // A 4xx client error (other than 408/429) means the request itself is the problem; retrying
        // won't help, so let it propagate and be dead-lettered.
        if (failure is ApiException apiFailure && IsPermanentClientError(apiFailure.StatusCode))
            return false;

        // Everything else — 5xx, throttling/timeout (408/429), and transport failures — is transient.
        return true;
    }

    private static bool IsPermanentClientError(HttpStatusCode statusCode) =>
        (int)statusCode is >= 400 and < 500
        && statusCode is not HttpStatusCode.RequestTimeout   // 408 — worth retrying
        && statusCode is not HttpStatusCode.TooManyRequests; // 429 — worth retrying after backoff
}
