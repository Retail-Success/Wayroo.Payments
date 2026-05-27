namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

/// <summary>
/// A queue message describing a payment provider configuration to record. The upstream
/// API Gateway -> SQS integration resolves the routing identifiers (store/provider/tenant) from the
/// provider's account id and forwards the provider payload verbatim.
/// </summary>
/// <remarks>
/// This contract is provisional and will be firmed up when the API Gateway -> SQS integration that
/// feeds this queue is designed. <see cref="StoreId"/> and <see cref="ProviderId"/> are required; the
/// remaining fields are persisted as-is when supplied.
/// </remarks>
public class PaymentConfigurationMessage
{
    /// <summary>Store the configuration belongs to. Table partition key. Required.</summary>
    public long? StoreId { get; set; }

    /// <summary>Provider the configuration is for (e.g. "propay"). Table sort key. Required.</summary>
    public string? ProviderId { get; set; }

    /// <summary>Tenant the store belongs to.</summary>
    public long? TenantId { get; set; }

    /// <summary>Provider account id. Indexed by the account-id GSI for reconciliation.</summary>
    public string? AccountId { get; set; }

    /// <summary>The provider payload, stored verbatim as the configuration.</summary>
    public string? ProviderConfiguration { get; set; }
}
