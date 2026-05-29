using Amazon.Lambda.SQSEvents;
using Microsoft.Extensions.Logging;
using Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

/// <summary>
/// Generic orchestration for the recorder lambda — provider-agnostic.
/// <list type="number">
///   <item><description>Ask <see cref="IPaymentConfigurationParser"/> to pull the account number,
///   provider id, and configuration payload out of the raw provider webhook body.</description></item>
///   <item><description>Ask <see cref="IStoreOwnerResolver"/> which Wayroo store + tenant own that
///   account.</description></item>
///   <item><description>Persist a <see cref="PaymentProviderConfiguration"/> row.</description></item>
/// </list>
/// Anything provider-specific lives behind the two seams above. Adding a new gateway is a matter of
/// dropping a new pair of implementations under <c>Gateways/{Provider}/</c> and registering them.
/// </summary>
/// <remarks>
/// Multi-gateway evolution: today the handler resolves a single <see cref="IPaymentConfigurationParser"/>
/// + <see cref="IStoreOwnerResolver"/> pair straight from DI because we only handle one provider. When
/// the upstream webhook envelope starts carrying a provider identifier (the gateway will dispatch with
/// it), introduce a small <c>IPaymentGatewayDispatcher</c> seam that looks at the body, picks the right
/// parser/resolver pair for that provider, and hand <em>that</em> to this handler instead of injecting
/// the pair directly. The parse → resolve → upsert shape doesn't change; the dispatcher is the only new
/// moving part. Registration moves from <c>AddSingleton&lt;I, T&gt;()</c> to either keyed registrations
/// or <c>IEnumerable&lt;I&gt;</c> with each implementation exposing a <c>bool CanHandle(...)</c>
/// predicate the dispatcher can call.
/// </remarks>
public class PaymentConfigurationMessageHandler(
    IPaymentConfigurationParser parser,
    IStoreOwnerResolver storeOwnerResolver,
    IPaymentConfigurationRepository repository,
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
    }
}
