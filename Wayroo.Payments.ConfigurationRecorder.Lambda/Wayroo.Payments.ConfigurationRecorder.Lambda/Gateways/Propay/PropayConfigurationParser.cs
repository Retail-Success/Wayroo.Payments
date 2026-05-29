using System.Text.Json;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways.Propay;

/// <summary>
/// ProPay-specific <see cref="IPaymentConfigurationParser"/>: extracts <c>payload.accountNum</c> out of
/// the MerchantWare/ProPay webhook body. The provider id is hard-coded to <c>"propay"</c> until the
/// upstream EventBridge integration starts dispatching the provider explicitly; the entire raw body is
/// returned as the configuration so no provider-supplied field is lost when it lands on the table.
/// </summary>
public class PropayConfigurationParser : IPaymentConfigurationParser
{
    private const string ProviderId = "propay";

    public ParsedPaymentConfiguration Parse(string rawWebhookBody)
    {
        if (!TryReadAccountNumber(rawWebhookBody, out var accountNumber))
        {
            throw new FormatException("ProPay webhook body is missing a valid payload.accountNum.");
        }

        return new ParsedPaymentConfiguration(
            AccountNumber: accountNumber,
            ProviderId: ProviderId,
            Configuration: rawWebhookBody);
    }

    /// <summary>
    /// Parses <c>payload.accountNum</c> from the body. Accepts the field as either a JSON string (the
    /// real provider webhook always sends it quoted) or a number (so the unit/integration/smoke tests
    /// can emit a numeric literal without escaping).
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
