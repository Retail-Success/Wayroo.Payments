namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

/// <summary>
/// The envelope we intend to receive on the configuration queue once the upstream API Gateway → SQS (and
/// eventually EventBridge) integration is firmed up: routing data already resolved (account number,
/// provider) and the provider payload carried alongside as a separate field.
/// </summary>
/// <remarks>
/// Currently the recorder lambda does NOT deserialize messages with this type — until the upstream
/// integration starts enriching messages, <see cref="PaymentConfigurationMessageHandler"/> parses the raw
/// provider webhook body directly (extracting <c>payload.accountNum</c>) and stores the whole body
/// verbatim as the configuration. This DTO is preserved as the target contract so the swap-over to a
/// typed envelope is a small change when the upstream is ready.
/// </remarks>
public class PaymentConfigurationMessage
{
    /// <summary>The provider account number, used to resolve the store/tenant from the Orders API.</summary>
    public long? AccountNumber { get; set; }

    /// <summary>Provider the configuration is for (e.g. "propay"). Sort key on the DynamoDB table.</summary>
    public string? ProviderId { get; set; }

    /// <summary>The provider payload, stored verbatim as the configuration.</summary>
    public string? ProviderConfiguration { get; set; }
}
