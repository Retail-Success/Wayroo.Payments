using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using Luci.Orders.SDK.Api;
using Microsoft.Extensions.Logging;
using Wayroo.Common.Exceptions;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

/// <summary>
/// Resolves the store/tenant for a ProPay account (via the Orders API), then upserts the configuration.
/// </summary>
/// <remarks>
/// The SQS message body is the raw provider webhook — we don't yet know the full payload-gateway shape,
/// so we only pluck the account number out of <c>payload.accountNum</c> and persist the entire body
/// verbatim in <c>ProviderConfiguration</c>, leaving every provider-supplied field available downstream.
/// </remarks>
public class PaymentConfigurationMessageHandler(
    IStorePropayClient storePropayClient,
    IPaymentConfigurationRepository repository,
    ILogger<PaymentConfigurationMessageHandler> logger) : IMessageHandler
{
    /// <summary>
    /// Hard-coded until the upstream EventBridge integration starts dispatching the provider. Every
    /// message we currently receive is a ProPay configuration webhook.
    /// </summary>
    private const string ProviderId = "propay";

    public async Task Handle(SQSEvent.SQSMessage message, CancellationToken cancellationToken)
    {
        if (!TryReadAccountNumber(message.Body, out var accountNumber))
        {
            // Permanent: a malformed body can never succeed, send to the dead-letter queue.
            throw new FormatException(
                $"Message {message.MessageId} is missing a valid payload.accountNum.");
        }

        var owner = await storePropayClient.GetStoreIdFromPropayAccountNumberAsync(accountNumber, cancellationToken);
        if (owner is null || owner.StoreId <= 0)
        {
            // Transient: the account isn't onboarded yet — re-queue and try again.
            throw new ResourceAccessException(
                $"No store resolved for ProPay account {accountNumber}; re-queueing to retry.");
        }

        var configuration = new PaymentProviderConfiguration
        {
            StoreId = owner.StoreId,
            TenantId = owner.TenantId,
            ProviderId = ProviderId,
            AccountId = accountNumber.ToString(),
            ProviderConfiguration = message.Body,
        };

        // Never log message.Body — it carries payment credentials.
        logger.LogInformation(
            "Resolved ProPay account {AccountNumber} to store {StoreId} tenant {TenantId}; recording {ProviderId} configuration.",
            accountNumber,
            owner.StoreId,
            owner.TenantId,
            ProviderId);

        await repository.UpsertConfiguration(configuration, cancellationToken);
    }

    /// <summary>
    /// Parses <c>payload.accountNum</c> from the webhook body. Accepts the field as either a JSON string
    /// (the real provider webhook) or number (so the smoke / integration tests can send a numeric literal).
    /// </summary>
    private static bool TryReadAccountNumber(string body, out long accountNumber)
    {
        accountNumber = 0;

        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("payload", out var payload)
                || !payload.TryGetProperty("accountNum", out var accountNum))
            {
                return false;
            }

            var parsed = accountNum.ValueKind switch
            {
                JsonValueKind.Number => accountNum.TryGetInt64(out accountNumber),
                JsonValueKind.String => long.TryParse(accountNum.GetString(), out accountNumber),
                _ => false,
            };

            return parsed && accountNumber > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
