namespace Wayroo.Payments.ConfigurationRecorder.Lambda.Gateways;

/// <summary>
/// Reads the routing data out of a raw provider webhook body. One implementation per payment
/// gateway lives under <c>Gateways/{Provider}/</c> (see <c>Propay</c> for the current example) — to add
/// a new gateway, drop in a new parser there and register it in <see cref="Function"/>.
/// </summary>
public interface IPaymentConfigurationParser
{
    /// <summary>
    /// Extracts the account number, provider id, and configuration payload from the supplied
    /// provider webhook body.
    /// </summary>
    /// <exception cref="FormatException">
    /// The body is malformed or missing required fields. Non-transient → the recorder lambda's
    /// <see cref="ProcessFailureHandler"/> sends the message to the dead-letter queue.
    /// </exception>
    ParsedPaymentConfiguration Parse(string rawWebhookBody);
}

/// <summary>
/// The shape produced by a <see cref="IPaymentConfigurationParser"/>: the routing key the handler needs
/// to resolve store/tenant ownership, plus the verbatim configuration payload to persist.
/// </summary>
/// <param name="AccountNumber">The provider account number (used to resolve the store/tenant).</param>
/// <param name="ProviderId">The provider this configuration belongs to (e.g. <c>"propay"</c>).</param>
/// <param name="Configuration">The configuration payload to persist verbatim on the DynamoDB row.</param>
public sealed record ParsedPaymentConfiguration(long AccountNumber, string ProviderId, string Configuration);
