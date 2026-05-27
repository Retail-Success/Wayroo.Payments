namespace Wayroo.Payments.Models;

/// <summary>
/// A configuration received from a payment provider, persisted to the payment configuration table.
/// One record exists per store per provider (partitioned by <see cref="StoreId"/>, sorted by
/// <see cref="ProviderId"/>). The original provider payload is stored verbatim.
/// </summary>
public class PaymentProviderConfiguration
{
    /// <summary>
    /// The store the configuration belongs to. Partition key.
    /// NOTE: this assumes <see cref="StoreId"/> is globally unique across tenants. If a store id can
    /// repeat under different tenants, the partition key must fold in <see cref="TenantId"/>.
    /// </summary>
    public long StoreId { get; set; }

    /// <summary>The payment provider this configuration is for (e.g. "propay"). Sort key.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// The provider's account identifier (from the payload). The configuration's stable identity at the
    /// provider — unchanged across credential rotations — so it backs a global secondary index that lets
    /// a record be found without knowing its store + provider. Only written when present (sparse index).
    /// </summary>
    public string? AccountId { get; set; }

    /// <summary>The tenant the store belongs to. Resolved upstream (not present in the provider webhook).</summary>
    public long? TenantId { get; set; }

    /// <summary>The original configuration payload, stored verbatim as received.</summary>
    public string? ProviderConfiguration { get; set; }

    /// <summary>When the configuration was first recorded.</summary>
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>When the record was last written.</summary>
    public DateTimeOffset? ModifiedOn { get; set; }
}
