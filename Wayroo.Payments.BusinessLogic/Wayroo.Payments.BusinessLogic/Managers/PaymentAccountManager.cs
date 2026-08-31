using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wayroo.Payments.BusinessLogic.Gateways;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.Managers;

/// <summary>
/// Works out which payment provider a store is on, then asks that provider's gateway.
/// </summary>
/// <remarks>
/// <para>
/// The resolution rule, in order:
/// </para>
/// <list type="number">
///   <item><description>
///   An explicit <c>providerId</c> wins, provided a gateway is registered for it. This exists for
///   support ("what does the old provider still say about this store?"), not for ordinary callers.
///   </description></item>
///   <item><description>
///   Otherwise the store's recorded routing decides —
///   <see cref="StoreRoutingConfiguration.AcquiringProviderId"/>, the platform's single source of
///   truth for which provider a store acquires through.
///   </description></item>
///   <item><description>
///   No routing record falls through to <see cref="PaymentGatewayOptions.DefaultProviderId"/>. This is
///   the common case, not an edge: a record only appears once the recorder has seen the store. It is
///   also what makes the whole mechanism inert — nothing needs backfilling for existing stores to keep
///   resolving exactly as they did.
///   </description></item>
/// </list>
/// <para>
/// A routing value naming a provider with no gateway raises rather than quietly falling back: the
/// store has been deliberately pointed somewhere this service cannot reach, and silently serving it
/// from the old provider would be the wrong answer given confidently.
/// </para>
/// </remarks>
public class PaymentAccountManager(
    IPaymentConfigurationRepository repository,
    IPaymentGatewayRegistry registry,
    IOptions<PaymentGatewayOptions> options,
    ILogger<PaymentAccountManager> logger) : IPaymentAccountManager
{
    /// <inheritdoc />
    public async Task<PaymentAccountBalance> GetBalance(
        long tenantId,
        long storeId,
        string? providerId,
        CancellationToken cancellationToken)
    {
        var gateway = await ResolveGateway(storeId, providerId, cancellationToken);

        var balance = await gateway.GetBalance(tenantId, storeId, cancellationToken);

        if (balance is not null)
            return balance;

        logger.LogInformation(
            "No {ProviderId} account exists for store {StoreId} (tenant {TenantId}).",
            gateway.ProviderId,
            storeId,
            tenantId);

        return new PaymentAccountBalance { AccountExists = false, ProviderId = gateway.ProviderId };
    }

    /// <inheritdoc />
    public async Task<PaymentAccountDetails> RefreshAccount(
        long tenantId,
        long storeId,
        string? providerId,
        string? providerAccountRef,
        CancellationToken cancellationToken)
    {
        var gateway = await ResolveGateway(storeId, providerId, cancellationToken);

        var details = await gateway.RefreshAccountDetails(
            tenantId,
            storeId,
            providerAccountRef,
            cancellationToken);

        if (details is null)
        {
            // A backfill sweeping every store expects to meet stores that never onboarded, or whose
            // account reference it does not know, and should be able to record that and move on rather
            // than treat it as a failed item.
            //
            // Carries PaymentsLogSignals.RefreshAccountNoAccount so the CloudWatch metric filter in
            // the PaymentsAPI construct can count these without depending on this sentence's wording.
            // Deliberately still Information: one of these is ordinary, and only the rate is alarming.
            logger.LogInformation(
                "{PaymentsSignal}: no {ProviderId} account exists for store {StoreId} (tenant {TenantId}); nothing recorded.",
                PaymentsLogSignals.RefreshAccountNoAccount,
                gateway.ProviderId,
                storeId,
                tenantId);

            return new PaymentAccountDetails { AccountExists = false, ProviderId = gateway.ProviderId };
        }

        logger.LogInformation(
            "Refreshed {ProviderId} account details for store {StoreId} (tenant {TenantId}); status {AccountStatus}.",
            gateway.ProviderId,
            storeId,
            tenantId,
            details.Status);

        return details;
    }

    private async Task<IPaymentAccountGateway> ResolveGateway(
        long storeId,
        string? requestedProviderId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedProviderId))
        {
            if (registry.TryGet(requestedProviderId, out var requested))
                return requested;

            throw new PaymentProviderNotSupportedException(requestedProviderId, registry.KnownProviderIds);
        }

        var routing = await repository.GetRouting(storeId, cancellationToken);

        if (routing is null || string.IsNullOrWhiteSpace(routing.AcquiringProviderId))
        {
            var defaultProviderId = options.Value.DefaultProviderId;

            if (registry.TryGet(defaultProviderId, out var fallback))
            {
                logger.LogInformation(
                    "No routing recorded for store {StoreId}; assuming the default provider {ProviderId}.",
                    storeId,
                    fallback.ProviderId);
                return fallback;
            }

            throw new PaymentProviderAmbiguousException(
                $"Store {storeId} has no recorded routing and the configured default provider "
                + $"'{defaultProviderId}' has no gateway. Known providers: "
                + $"{string.Join(", ", registry.KnownProviderIds)}.",
                storeId);
        }

        if (registry.TryGet(routing.AcquiringProviderId, out var gateway))
            return gateway;

        throw new PaymentProviderAmbiguousException(
            $"Store {storeId} is routed to provider '{routing.AcquiringProviderId}', which has no "
            + $"gateway. Known providers: {string.Join(", ", registry.KnownProviderIds)}.",
            storeId,
            [routing.AcquiringProviderId]);
    }
}
