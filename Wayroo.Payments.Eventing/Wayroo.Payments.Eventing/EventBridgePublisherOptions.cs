namespace Wayroo.Payments.Eventing;

/// <summary>
/// Settings for <see cref="EventBridgeIntegrationEventPublisher"/>.
/// </summary>
public sealed class EventBridgePublisherOptions
{
    /// <summary>
    /// ARN of the intraprocess event bus entries are published to. Deliberately has no default:
    /// publishing to the wrong bus (or to <c>default</c>, which is what an empty value resolves to
    /// in EventBridge) is silent, so the host must supply it.
    /// </summary>
    /// <remarks>
    /// An ARN rather than a bare name: <c>PutEvents</c> accepts either, and the ARN is unambiguous
    /// about which account's bus is meant.
    /// </remarks>
    public required string EventBusArn { get; init; }
}
