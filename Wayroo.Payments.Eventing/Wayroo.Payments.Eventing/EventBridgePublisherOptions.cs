using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.Eventing;

/// <summary>
/// Settings for <see cref="EventBridgeIntegrationEventPublisher"/>.
/// </summary>
/// <remarks>
/// Bound from configuration in two passes — see <c>AddPaymentsEventPublishing</c>. The bus ARN and
/// the region come from the configuration <b>root</b>;
/// <see cref="AwsClientOptions.ServiceUrl"/> comes from the <see cref="SectionName"/> section, so it
/// is <c>EventBridge:ServiceUrl</c>.
/// </remarks>
public sealed class EventBridgePublisherOptions : AwsClientOptions
{
    /// <inheritdoc cref="EventingConfigurationKeys.EventBridgeSection" />
    public const string SectionName = EventingConfigurationKeys.EventBridgeSection;

    /// <summary>
    /// ARN of the intraprocess event bus entries are published to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An ARN rather than a bare name: <c>PutEvents</c> accepts either, and the ARN is unambiguous
    /// about which account's bus is meant.
    /// </para>
    /// <para>
    /// The property is named for what the publisher needs while the configuration key stays
    /// <c>WayrooEventsBusArn</c> — the name the CDK, both hosts' <c>EnvironmentVariableKeys</c> and the
    /// stack parameter all already use. <see cref="ConfigurationKeyNameAttribute"/> bridges the two so
    /// neither has to be renamed. It needs a compile-time constant, which is why it points at
    /// <see cref="EventingConfigurationKeys"/> (whose members are <c>const</c>) and not at a host's
    /// <c>EnvironmentVariableKeys</c> (whose members are <c>static readonly</c>).
    /// </para>
    /// </remarks>
    // Empty rather than `required`: the options pipeline constructs the instance with new() and then
    // binds onto it, so `required` guarantees nothing at runtime while telling the nullable analyzer
    // this can never be null — which it can, whenever configuration is missing. Validation is the
    // real guarantee, and it has to be here because publishing to an empty ARN is silent: EventBridge
    // resolves it to the account's default bus. Non-blank only, no ARN-format check — the lambda's
    // integration tests set every key to junk to prove the function constructs.
    [ConfigurationKeyName(EventingConfigurationKeys.WayrooEventsBusArn)]
    [Required(ErrorMessage =
        $"Missing required configuration: {EventingConfigurationKeys.WayrooEventsBusArn}. "
        + "The CDK passes it through from the WayrooEventsBusArn stack parameter.")]
    public string EventBusArn { get; set; } = string.Empty;
}
