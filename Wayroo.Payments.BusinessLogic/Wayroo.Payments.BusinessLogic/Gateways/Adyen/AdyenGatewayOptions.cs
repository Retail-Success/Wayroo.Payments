using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.Gateways.Adyen;

/// <summary>
/// What this service needs in order to talk to Adyen.
/// </summary>
/// <remarks>
/// <para>
/// Adyen issues a credential per API scope rather than one per company, so the account and
/// configuration planes authenticate separately: a key carrying Legal Entity Management roles is
/// rejected outright by the balance platform, and vice versa. They are held apart here for that
/// reason, not as a precaution.
/// </para>
/// <para>
/// Credentials arrive from Parameter Store at runtime; nothing here has a default, because a blank
/// key is indistinguishable at the call site from a key with the wrong roles — both surface as a 401
/// on the first request.
/// </para>
/// </remarks>
public class AdyenGatewayOptions
{
    /// <summary>
    /// Whether this host talks to Adyen at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off by default, and deliberately a switch rather than an inference from whether the
    /// credentials happen to be present. Adyen credentials do not exist in every environment yet, and
    /// a host that guessed from configuration would start happily with Adyen silently disabled — the
    /// exact failure the startup validation exists to prevent.
    /// </para>
    /// <para>
    /// So: while this is off, nothing Adyen is registered and an Adyen request is refused as an
    /// unsupported provider. Once it is on, every credential is validated at startup and a missing one
    /// fails the deployment rather than a merchant's first request.
    /// </para>
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// The API key for Legal Entity Management — legal entities, business lines, hosted onboarding
    /// links, terms of service.
    /// </summary>
    public string LegalEntityApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The API key for the balance platform Configuration API — account holders, balance accounts,
    /// sweeps.
    /// </summary>
    public string BalancePlatformApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The balance platform this environment's accounts live under.
    /// </summary>
    public string BalancePlatformId { get; set; } = string.Empty;

    /// <summary>
    /// Whether to call Adyen's live endpoints rather than its test ones.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ReferenceEnvironment"/> on purpose: dev and QA are different
    /// environments of ours that both run against Adyen's test platform.
    /// </remarks>
    public bool UseLiveEndpoints { get; set; }

    /// <summary>
    /// Required when <see cref="UseLiveEndpoints"/> is set — Adyen's live endpoints are prefixed per
    /// customer and there is no usable default.
    /// </summary>
    public string? LiveEndpointUrlPrefix { get; set; }

    /// <summary>
    /// The per-tenant settings, keyed by tenant identifier as text.
    /// </summary>
    /// <remarks>
    /// Keyed by text because that is what a configuration key is, and the values arrive from
    /// Parameter Store one leaf at a time. A tenant with no entry cannot be onboarded, which is the
    /// intended behaviour rather than a gap — see <see cref="AdyenTenantOptions"/>.
    /// </remarks>
    public Dictionary<string, AdyenTenantOptions> Tenants { get; init; } = [];

    /// <summary>
    /// Which of our environments to stamp on the references we write.
    /// </summary>
    /// <remarks>
    /// Dev and QA share one Adyen test platform, so this letter is the only thing separating their
    /// records. Getting it wrong writes references that read as another environment's.
    /// </remarks>
    public AdyenReferenceEnvironment ReferenceEnvironment { get; set; } = AdyenReferenceEnvironment.Dev;
}
