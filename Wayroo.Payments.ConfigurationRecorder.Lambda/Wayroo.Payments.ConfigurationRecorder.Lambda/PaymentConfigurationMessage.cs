namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

/// <summary>
/// A queue message describing a payment provider configuration to record. The store and tenant are NOT
/// carried on the message — they are resolved at processing time from the provider account number via the
/// Orders API. The upstream API Gateway -> SQS integration only needs to forward the account number, the
/// provider, and the provider payload.
/// </summary>
/// <remarks>
/// This contract is provisional and will be firmed up when the API Gateway -> SQS integration that feeds
/// this queue is designed. <see cref="AccountNumber"/> and <see cref="ProviderId"/> are required.
/// </remarks>
public class PaymentConfigurationMessage
{
    /// <summary>The provider account number, used to resolve the store/tenant from the Orders API. Required.</summary>
    public long? AccountNumber { get; set; }

    /// <summary>Provider the configuration is for (e.g. "propay"). Table sort key. Required.</summary>
    public string? ProviderId { get; set; }

    /// <summary>The provider payload, stored verbatim as the configuration.</summary>
    public string? ProviderConfiguration { get; set; }
}
