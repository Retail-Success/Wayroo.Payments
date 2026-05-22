namespace Wayroo.Payments.Models;

/// <summary>
/// A configuration received from a payment provider, persisted to the payment configuration table.
/// One record exists per provider per tenant (partitioned by <see cref="TenantId"/>, sorted by
/// <see cref="Provider"/>).
/// </summary>
public class PaymentProviderConfiguration
{
    /// <summary>The tenant the configuration belongs to. Partition key.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The payment provider this configuration is for (e.g. "stripe"). Sort key.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The original configuration payload, stored verbatim as received.</summary>
    public string? RawPayload { get; set; }

    /// <summary>When the configuration payload was first received/recorded.</summary>
    public DateTimeOffset? ReceivedTimestamp { get; set; }

    /// <summary>When the record was last written.</summary>
    public DateTimeOffset? LastModifiedTimestamp { get; set; }
}
