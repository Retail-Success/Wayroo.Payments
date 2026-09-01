using Amazon.Lambda.SQSEvents;
using Microsoft.Extensions.Logging;
using Wayroo.Common.Models.Events;
using Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways;
using Wayroo.Payments.Messages;
using Wayroo.Payments.Models;
// Both packages declare MigrationStates — the event contract needs it for consumers who take only
// Wayroo.Payments.Messages, the model needs it for persistence. The values are identical and held
// that way by MigrationStateParityTests; this end is the persistence one.
using MigrationStates = Wayroo.Payments.Models.MigrationStates;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

/// <summary>
/// Generic orchestration for the recorder lambda — provider-agnostic.
/// <list type="number">
///   <item><description>Ask <see cref="IPaymentConfigurationParser"/> to pull the account number,
///   provider id, and configuration payload out of the raw provider webhook body.</description></item>
///   <item><description>Ask <see cref="IStoreOwnerResolver"/> which Wayroo store + tenant own that
///   account.</description></item>
///   <item><description>Persist a <see cref="PaymentProviderConfiguration"/> row.</description></item>
///   <item><description>Make sure the store has a routing record, and announce it if this write moved
///   it.</description></item>
/// </list>
/// Anything provider-specific lives behind the two seams above. Adding a new gateway is a matter of
/// dropping a new pair of implementations under <c>Gateways/{Provider}/</c> and registering them.
/// </summary>
/// <remarks>
/// <para>
/// Multi-gateway evolution: today the handler resolves a single <see cref="IPaymentConfigurationParser"/>
/// + <see cref="IStoreOwnerResolver"/> pair straight from DI because we only handle one provider. When
/// the upstream webhook envelope starts carrying a provider identifier (the gateway will dispatch with
/// it), introduce a small <c>IPaymentGatewayDispatcher</c> seam that looks at the body, picks the right
/// parser/resolver pair for that provider, and hand <em>that</em> to this handler instead of injecting
/// the pair directly. The parse → resolve → upsert shape doesn't change; the dispatcher is the only new
/// moving part. Registration moves from <c>AddSingleton&lt;I, T&gt;()</c> to either keyed registrations
/// or <c>IEnumerable&lt;I&gt;</c> with each implementation exposing a <c>bool CanHandle(...)</c>
/// predicate the dispatcher can call.
/// </para>
/// <para>
/// <b>Routing announcements.</b> Seeing a store for the first time seeds its routing at the platform
/// default, which is what keeps this deploy inert: every store resolves to the provider it was already
/// on. <see cref="StoreProviderConfigChanged"/> goes out only when the routing fact actually moved —
/// a credential webhook that changes nothing about routing is silent, so consumers are not made to
/// dedupe traffic that carries no news.
/// </para>
/// </remarks>
public class PaymentConfigurationMessageHandler(
    IPaymentConfigurationParser parser,
    IStoreOwnerResolver storeOwnerResolver,
    IPaymentConfigurationRepository repository,
    IIntegrationEventPublisher publisher,
    ILogger<PaymentConfigurationMessageHandler> logger) : IMessageHandler
{
    public async Task Handle(SQSEvent.SQSMessage message, CancellationToken cancellationToken)
    {
        var parsed = parser.Parse(message.Body);
        var owner = await storeOwnerResolver.Resolve(parsed.AccountNumber, cancellationToken);

        var configuration = new PaymentProviderConfiguration
        {
            StoreId = owner.StoreId,
            TenantId = owner.TenantId,
            ProviderId = parsed.ProviderId,
            AccountId = parsed.AccountNumber.ToString(),
            ProviderConfiguration = parsed.Configuration,
        };

        // Never log message.Body / parsed.Configuration — it carries payment credentials.
        logger.LogInformation(
            "Recording {ProviderId} configuration for store {StoreId} tenant {TenantId} (AccountId {AccountId}).",
            parsed.ProviderId,
            owner.StoreId,
            owner.TenantId,
            configuration.AccountId);

        await repository.UpsertConfiguration(configuration, cancellationToken);

        await AnnounceRouting(owner, cancellationToken);
    }

    /// <summary>
    /// Ensures the store has a routing record and publishes when this write moved it.
    /// </summary>
    /// <remarks>
    /// Published after the write, deliberately. A publish failure re-queues the message and the write
    /// repeats idempotently, which is the safe direction; announcing first and then failing to write
    /// would tell consumers about a state this service does not hold.
    /// <para>
    /// A re-delivery cannot reuse the envelope instance, so the retry carries a fresh
    /// <c>EventId</c> and consumer-side dedupe on that alone will not catch it. What does catch it is
    /// the sequence: the routing version is unchanged by a repeat, so the duplicate reads as stale.
    /// </para>
    /// </remarks>
    private async Task AnnounceRouting(StoreOwner owner, CancellationToken cancellationToken)
    {
        var existing = await repository.GetRouting(owner.StoreId, cancellationToken);

        var result = await repository.UpsertRouting(
            new StoreRoutingConfiguration
            {
                StoreId = owner.StoreId,
                TenantId = owner.TenantId,
                // Seeded at the platform default on first sight, and otherwise left exactly as it is:
                // a credential webhook is not an instruction to move a store between providers.
                AcquiringProviderId = existing?.AcquiringProviderId ?? ProviderIds.Propay,
                MigrationState = existing?.MigrationState ?? MigrationStates.PropayActive,
            },
            cancellationToken);

        if (!result.Changed)
        {
            logger.LogDebug(
                "Routing for store {StoreId} is unchanged ({AcquiringProviderId}); nothing announced.",
                owner.StoreId,
                result.Current.AcquiringProviderId);
            return;
        }

        var envelope = IntegrationEnvelope.ForStore(
            new StoreProviderConfigChanged
            {
                AcquiringProviderId = result.Current.AcquiringProviderId,
                MigrationState = result.Current.MigrationState,
                PreviousAcquiringProviderId = result.Previous?.AcquiringProviderId,
            },
            tenantId: owner.TenantId.ToString(),
            storeId: owner.StoreId.ToString(),
            correlationId: IntegrationEnvelope.NewCorrelationId(),
            sequence: result.Current.ConfigurationVersion);

        logger.LogInformation(
            "Announcing routing for store {StoreId}: {AcquiringProviderId} (was {PreviousAcquiringProviderId}), version {ConfigurationVersion}.",
            owner.StoreId,
            result.Current.AcquiringProviderId,
            result.Previous?.AcquiringProviderId ?? "unset",
            result.Current.ConfigurationVersion);

        await publisher.PublishAsync(envelope, cancellationToken);
    }
}
