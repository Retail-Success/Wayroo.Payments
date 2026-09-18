namespace Wayroo.Payments.BusinessLogic.Gateways.Adyen;

/// <summary>
/// The configuration keys the Adyen gateway reads. Declared next to the code that binds them, so a
/// host validating them at startup and the binder can never drift — the same arrangement
/// <see cref="PaymentGatewayConfigurationKeys"/> uses.
/// </summary>
public static class AdyenGatewayConfigurationKeys
{
    /// <summary>The section holding every Adyen setting.</summary>
    public const string Section = "Adyen";

    /// <summary>Whether this host talks to Adyen at all. Optional; off otherwise.</summary>
    public const string Enabled = $"{Section}:{nameof(AdyenGatewayOptions.Enabled)}";

    /// <summary>
    /// The Parameter Store path the Adyen credentials are read from. Optional.
    /// </summary>
    /// <remarks>
    /// A path rather than the secrets themselves, matching how the ProPay credentials are sourced:
    /// they exist only in Parameter Store, because they must never be committed.
    /// </remarks>
    public const string SecretsPath = $"{Section}:SecretsPath";

    /// <summary>The Legal Entity Management API key. Required.</summary>
    public const string LegalEntityApiKey = $"{Section}:{nameof(AdyenGatewayOptions.LegalEntityApiKey)}";

    /// <summary>The balance platform Configuration API key. Required, and distinct from the LEM key.</summary>
    public const string BalancePlatformApiKey = $"{Section}:{nameof(AdyenGatewayOptions.BalancePlatformApiKey)}";

    /// <summary>The balance platform accounts are created under. Required.</summary>
    public const string BalancePlatformId = $"{Section}:{nameof(AdyenGatewayOptions.BalancePlatformId)}";

    /// <summary>Whether to call Adyen's live endpoints. Optional; test endpoints otherwise.</summary>
    public const string UseLiveEndpoints = $"{Section}:{nameof(AdyenGatewayOptions.UseLiveEndpoints)}";

    /// <summary>The customer-specific live endpoint prefix. Required only for live.</summary>
    public const string LiveEndpointUrlPrefix = $"{Section}:{nameof(AdyenGatewayOptions.LiveEndpointUrlPrefix)}";

    /// <summary>Which environment letter to stamp on written references. Optional; dev otherwise.</summary>
    public const string ReferenceEnvironment = $"{Section}:{nameof(AdyenGatewayOptions.ReferenceEnvironment)}";

    /// <summary>The per-tenant settings. Required for every tenant that onboards stores.</summary>
    public const string Tenants = $"{Section}:{nameof(AdyenGatewayOptions.Tenants)}";

    /// <summary>One tenant's settings — see <see cref="AdyenTenantOptions"/>.</summary>
    /// <param name="tenantId">The tenant the settings belong to.</param>
    public static string TenantSection(long tenantId) => $"{Tenants}:{tenantId}";
}
