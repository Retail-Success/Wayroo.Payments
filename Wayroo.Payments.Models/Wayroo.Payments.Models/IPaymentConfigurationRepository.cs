namespace Wayroo.Payments.Models;

/// <summary>
/// Persistence for <see cref="PaymentProviderConfiguration"/> records.
/// </summary>
public interface IPaymentConfigurationRepository
{
    /// <summary>
    /// Inserts or updates the provider <i>credential</i> configuration for a store + provider:
    /// <see cref="PaymentProviderConfiguration.AccountId"/>,
    /// <see cref="PaymentProviderConfiguration.TenantId"/> and
    /// <see cref="PaymentProviderConfiguration.ProviderConfiguration"/>.
    /// </summary>
    /// <remarks>
    /// A merge, not a replace: attributes owned by <see cref="UpsertAccountDetails"/> are left
    /// untouched, so a credential webhook arriving after an account refresh does not erase the
    /// recorded account information.
    /// </remarks>
    Task<PaymentProviderConfiguration> UpsertConfiguration(
        PaymentProviderConfiguration configuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts or updates the provider <i>account information</i> for a store + provider:
    /// <see cref="PaymentProviderConfiguration.ProviderAccountDetails"/>,
    /// <see cref="PaymentProviderConfiguration.AccountStatus"/> and
    /// <see cref="PaymentProviderConfiguration.AccountDetailsRefreshedOn"/>, alongside the account
    /// and tenant identifiers.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="UpsertConfiguration"/>: it never writes
    /// <see cref="PaymentProviderConfiguration.ProviderConfiguration"/>, so refreshing an account
    /// cannot discard the credential payload the recorder wrote.
    /// </remarks>
    Task<PaymentProviderConfiguration> UpsertAccountDetails(
        PaymentProviderConfiguration configuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads which provider a store transacts through, or <c>null</c> when no routing has been
    /// recorded — in which case the caller falls back to the platform default.
    /// </summary>
    /// <param name="storeId">The store to read routing for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<StoreRoutingConfiguration?> GetRouting(long storeId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a store's routing and returns both what was there before and the record as it now
    /// stands, so a caller can tell whether anything actually changed.
    /// </summary>
    /// <remarks>
    /// One atomic write: the version is incremented server-side, so two concurrent writers cannot
    /// mint the same <see cref="StoreRoutingConfiguration.ConfigurationVersion"/> — which matters
    /// because that version is what downstream consumers use to discard a stale update.
    /// </remarks>
    /// <param name="routing">The routing to write. The version is assigned by the store, not the caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<StoreRoutingWriteResult> UpsertRouting(
        StoreRoutingConfiguration routing,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the configuration for a store + provider, or <c>null</c> if none exists.
    /// </summary>
    Task<PaymentProviderConfiguration?> GetConfiguration(
        long storeId,
        string providerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves every provider configuration recorded for a store (one per provider).
    /// </summary>
    Task<IReadOnlyList<PaymentProviderConfiguration>> GetConfigurationsForStore(
        long storeId,
        CancellationToken cancellationToken);
}
