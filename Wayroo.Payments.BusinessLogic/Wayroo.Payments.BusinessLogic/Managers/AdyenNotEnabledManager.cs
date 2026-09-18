using Wayroo.Payments.BusinessLogic.Gateways;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.Managers;

/// <summary>
/// Stands in for <see cref="IAdyenOnboardingManager"/> in a host that does not talk to Adyen.
/// </summary>
/// <remarks>
/// <para>
/// Registered instead of the real manager while <see cref="Gateways.Adyen.AdyenGatewayOptions.Enabled"/>
/// is off, so the endpoints exist everywhere and answer honestly rather than existing in some
/// environments and not others.
/// </para>
/// <para>
/// It refuses rather than pretending: a caller is told this service has no Adyen gateway, in the same
/// words it would get for any other provider it cannot reach. The alternative — leaving the service
/// unregistered — fails inside the request pipeline with a dependency-resolution error, which reaches
/// the caller as a 500 and reads like an outage.
/// </para>
/// </remarks>
public class AdyenNotEnabledManager(IPaymentGatewayRegistry registry) : IAdyenOnboardingManager
{
    // Must match ProviderIds.Adyen in Wayroo.Payments.Messages and the sort key the Adyen record
    // lives under. Spelled out rather than referenced, the same way the other tiers spell it: this
    // project takes no dependency on the messages package.
    private const string AdyenProviderId = "adyen";

    /// <inheritdoc />
    public Task<AdyenAccount> Onboard(
        long tenantId,
        long storeId,
        AdyenSellerDetails seller,
        CancellationToken cancellationToken) => throw NotEnabled();

    /// <inheritdoc />
    public Task<Uri?> GetOnboardingLink(
        long storeId,
        string? redirectUrl,
        CancellationToken cancellationToken) => throw NotEnabled();

    private PaymentProviderNotSupportedException NotEnabled()
        => new(AdyenProviderId, registry.KnownProviderIds);
}
