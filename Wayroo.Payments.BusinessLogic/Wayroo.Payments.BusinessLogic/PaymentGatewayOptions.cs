namespace Wayroo.Payments.BusinessLogic;

/// <summary>
/// Settings governing how a store's payment provider is chosen.
/// </summary>
public class PaymentGatewayOptions
{
    /// <summary>
    /// The provider <see cref="DefaultProviderId"/> falls back to when nothing is configured.
    /// </summary>
    /// <remarks>
    /// Read <see cref="DefaultProviderId"/>, not this — the property is what applies the "blank means
    /// unset" rule.
    /// </remarks>
    public const string PlatformDefaultProviderId = "propay";

    private string _defaultProviderId = PlatformDefaultProviderId;

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
    /// <para>
    /// Never blank: a blank assignment is read as "not configured" and leaves
    /// <see cref="PlatformDefaultProviderId"/> in place.
    /// </para>
    /// </remarks>
    public string DefaultProviderId
    {
        get => _defaultProviderId;

        // A blank value means "not configured", not "no provider". Binding from configuration skips
        // keys that are absent but happily writes one that is present and empty — which is exactly
        // what an ECS task definition or lambda environment entry set to "" produces — and that would
        // otherwise take the platform default away and leave the manager resolving a gateway named "".
        //
        // Enforced on the property rather than in a PostConfigure so the invariant holds however the
        // instance is produced, including a test doing Options.Create(new PaymentGatewayOptions { ... }).
        set => _defaultProviderId = string.IsNullOrWhiteSpace(value) ? PlatformDefaultProviderId : value;
    }
}
