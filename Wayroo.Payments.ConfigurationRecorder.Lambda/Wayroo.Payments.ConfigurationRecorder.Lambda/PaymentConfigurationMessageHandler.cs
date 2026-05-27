using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using Luci.Orders.SDK.Api;
using Microsoft.Extensions.Logging;
using Wayroo.Common.Exceptions;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

/// <summary>
/// Resolves the store/tenant for a ProPay account via the Orders API, then upserts the configuration.
/// </summary>
public class PaymentConfigurationMessageHandler(
    IStorePropayClient storePropayClient,
    IPaymentConfigurationRepository repository,
    ILogger<PaymentConfigurationMessageHandler> logger) : IMessageHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task Handle(SQSEvent.SQSMessage message, CancellationToken cancellationToken)
    {
        var configurationMessage = JsonSerializer.Deserialize<PaymentConfigurationMessage>(message.Body, SerializerOptions);

        if (configurationMessage is null
            || configurationMessage.AccountNumber is null or <= 0
            || string.IsNullOrWhiteSpace(configurationMessage.ProviderId))
        {
            // A malformed/incomplete message can never succeed. Throwing a non-transient exception sends
            // it to the dead-letter queue rather than retrying forever.
            throw new FormatException(
                $"Message {message.MessageId} is missing required fields (AccountNumber/ProviderId).");
        }

        var accountNumber = configurationMessage.AccountNumber.Value;

        var owner = await storePropayClient.GetStoreIdFromPropayAccountNumberAsync(accountNumber, cancellationToken);

        if (owner is null || owner.StoreId <= 0)
        {
            // The store/tenant aren't resolvable yet — most likely the account hasn't finished onboarding.
            // Treat as transient so the failure handler re-queues the message and we retry until it resolves.
            throw new ResourceAccessException(
                $"No store resolved for ProPay account {accountNumber}; re-queueing to retry.");
        }

        var configuration = new PaymentProviderConfiguration
        {
            StoreId = owner.StoreId,
            TenantId = owner.TenantId,
            ProviderId = configurationMessage.ProviderId,
            AccountId = accountNumber.ToString(),
            ProviderConfiguration = configurationMessage.ProviderConfiguration,
        };

        // Never log ProviderConfiguration — it carries payment credentials.
        logger.LogInformation(
            "Resolved ProPay account {AccountNumber} to store {StoreId} tenant {TenantId}; recording {ProviderId} configuration.",
            accountNumber,
            owner.StoreId,
            owner.TenantId,
            configuration.ProviderId);

        await repository.UpsertConfiguration(configuration, cancellationToken);
    }
}
