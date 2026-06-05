using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways.Propay;

/// <summary>
/// ProPay-specific <see cref="IPaymentConfigurationParser"/>: extracts <c>detail.payload.accountNum</c>
/// out of the EventBridge envelope the recorder's queue receives. The provider id is hard-coded to
/// <c>"propay"</c> until the upstream gateway dispatches the provider explicitly; the EventBridge
/// envelope's <c>detail</c> block (the actual provider webhook body) is returned as the configuration
/// so no provider-supplied field is lost when it lands on the table — the surrounding envelope
/// (<c>version</c>, <c>id</c>, <c>source</c>, <c>account</c>, <c>region</c>, …) is dropped since it's
/// delivery metadata, not provider data.
/// </summary>
/// <param name="logger">The observibility provider for unexepcted errors.</param>
public class PropayConfigurationParser(ILogger<PropayConfigurationParser> logger) : IPaymentConfigurationParser
{
    private const string ProviderId = "propay";

    public ParsedPaymentConfiguration Parse(string rawWebhookBody)
    {
        if (!TryReadDetail(rawWebhookBody, out var detailJson, out var accountNumber))
        {
            throw new FormatException("ProPay event body is missing a valid detail.payload.accountNum.");
        }

        return new ParsedPaymentConfiguration(
            AccountNumber: accountNumber,
            ProviderId: ProviderId,
            Configuration: detailJson);
    }

    /// <summary>
    /// Pulls the EventBridge <c>detail</c> block and its <c>payload.accountNum</c>. Accepts
    /// <c>accountNum</c> as either a JSON string (the real provider webhook always sends it quoted)
    /// or a number (so the unit/integration/smoke tests can emit a numeric literal without escaping).
    /// </summary>
    private bool TryReadDetail(string body, out string detailJson, out long accountNumber)
    {
        detailJson = string.Empty;
        accountNumber = 0;

        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("detail", out var detail)
                || !detail.TryGetProperty("payload", out var payload))
            {
                return false;
            }

            var payloadObject = payload.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(payload.GetString()!).RootElement
                : payload;

            if (!payloadObject.TryGetProperty("accountNum", out var accountNum))
            {
                return false;
            }

            var parsed = accountNum.ValueKind switch
            {
                JsonValueKind.Number => accountNum.TryGetInt64(out accountNumber),
                JsonValueKind.String => long.TryParse(accountNum.GetString(), out accountNumber),
                _ => false,
            };

            if (!parsed || accountNumber <= 0)
                return false;

            detailJson = payloadObject.ToString();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to parse detail or determine account number.");
            return false;
        }
    }
}
