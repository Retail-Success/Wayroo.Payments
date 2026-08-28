namespace Wayroo.Payments.Eventing;

/// <summary>
/// Configuration keys this library reads. Hosts surface them however they like (env vars for the
/// recorder lambda and the API's ECS task definition today) — the names are the contract.
/// </summary>
public static class EventingConfigurationKeys
{
    /// <summary>
    /// ARN of the intraprocess event bus integration events are published to
    /// (<c>{env}-wayroo-events</c>). Provisioned by the Infrastructure team's common project and
    /// imported by ARN in <c>Wayroo.Payments.Infrastructure</c> — not owned by this service.
    /// </summary>
    /// <remarks>
    /// Not to be confused with <c>{env}-webhook-bus</c>, the inbound bus the ConfigurationRecorder
    /// consumes provider webhooks from. This one is strictly outbound.
    /// </remarks>
    public const string WayrooEventsBusArn = nameof(WayrooEventsBusArn);

    /// <summary>AWS region the EventBridge client targets. Shared with the DynamoDB/SQS wiring.</summary>
    public const string AwsRegion = nameof(AwsRegion);
}
