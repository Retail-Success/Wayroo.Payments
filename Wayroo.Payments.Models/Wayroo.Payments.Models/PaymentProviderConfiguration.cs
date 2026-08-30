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
    /// The provider's account identifier. The configuration's stable identity at the provider —
    /// unchanged across credential rotations — and the key handed back to the provider when calling
    /// its account operations. Only written when present, so the attribute is sparse.
    /// </summary>
    /// <remarks>
    /// A global secondary index on this attribute would let a record be found without knowing its
    /// store + provider. None is provisioned today (see the CDK <c>PaymentConfigurationTable</c>); no
    /// current access pattern needs one.
    /// </remarks>
    public string? AccountId { get; set; }

    /// <summary>The tenant the store belongs to. Resolved upstream (not present in the provider webhook).</summary>
    public long? TenantId { get; set; }

    /// <summary>The original configuration payload, stored verbatim as received.</summary>
    public string? ProviderConfiguration { get; set; }

    /// <summary>
    /// The provider's account information, stored verbatim as read from its account-detail
    /// operation. Distinct from <see cref="ProviderConfiguration"/>, which holds the credential
    /// payload pushed by the provider's webhook; the two are written by different callers and
    /// neither overwrites the other.
    /// </summary>
    public string? ProviderAccountDetails { get; set; }

    /// <summary>
    /// The account's provider-neutral standing, as of <see cref="AccountDetailsRefreshedOn"/>.
    /// Derived from the provider's own status when the account detail was recorded.
    /// </summary>
    public PaymentAccountStatus? AccountStatus { get; set; }

    /// <summary>When <see cref="ProviderAccountDetails"/> was last read from the provider.</summary>
    public DateTimeOffset? AccountDetailsRefreshedOn { get; set; }

    /// <summary>When the configuration was first recorded.</summary>
    public DateTimeOffset? CreatedOn { get; set; }

    /// <summary>When the record was last written.</summary>
    public DateTimeOffset? ModifiedOn { get; set; }
}
