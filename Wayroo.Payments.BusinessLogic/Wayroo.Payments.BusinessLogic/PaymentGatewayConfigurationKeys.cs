namespace Wayroo.Payments.BusinessLogic;

/// <summary>
/// The configuration keys this tier reads. Declared here, next to the code that consumes them, so a
/// host validating them at startup and the code binding them can never drift — the same arrangement
/// <c>Wayroo.Payments.Eventing.EventingConfigurationKeys</c> uses with the recorder lambda.
/// </summary>
public static class PaymentGatewayConfigurationKeys
{
    /// <summary>
    /// Parameter Store path holding the per-tenant ProPay credentials. Points at the shared vendor
    /// path Luci.Orders already reads rather than a copy under this service, so the two can never end
    /// up on different halves of a credential rotation. Required.
    /// </summary>
    public const string PropaySecretsPath = nameof(PropaySecretsPath);

    /// <summary>The section holding the three ProPay/ProtectPay base URIs.</summary>
    public const string PropayApiBaseUrisSection = "PropayApiBaseUrisOptions";

    // The individual base URIs. All three are required: the gateway package builds a typed HttpClient
    // per API and only throws on a blank base address when that client is first constructed, so a
    // host that does not check them up front discovers the gap on a merchant's first request.
    public const string PropayRestBaseUrl = $"{PropayApiBaseUrisSection}:PropayRest";
    public const string PropayXmlBaseUrl = $"{PropayApiBaseUrisSection}:PropayXml";
    public const string ProtectPayRestBaseUrl = $"{PropayApiBaseUrisSection}:ProtectPayRest";

    /// <summary>
    /// The provider to assume for a store this service holds no record for. Optional; defaults to
    /// <see cref="PaymentGatewayOptions.DefaultProviderId"/>.
    /// </summary>
    public const string DefaultProviderId = nameof(DefaultProviderId);
}
