namespace Wayroo.Payments.BusinessLogic;

/// <summary>
/// Settings governing how a store's payment provider is chosen.
/// </summary>
public class PaymentGatewayOptions
{
    /// <summary>
    /// The provider to assume for a store this service holds no configuration record for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most stores have no record here yet — a record only appears once a provider webhook has landed
    /// or an account refresh has run, which is exactly what the backfill exists to fix. Treating an
    /// unrecorded store as having no merchant account would take the balance endpoint away from the
    /// majority of stores, so resolution falls through to this instead, and the gateway recovers the
    /// account number from the Orders API.
    /// </para>
    /// <para>
    /// A store this service has never recorded is by definition still on the provider the platform
    /// has always used. Change this when new stores start onboarding onto a different one.
    /// </para>
    /// </remarks>
    public string DefaultProviderId { get; set; } = "propay";
}
