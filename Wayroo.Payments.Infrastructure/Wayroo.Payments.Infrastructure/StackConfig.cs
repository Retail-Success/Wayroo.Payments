namespace Wayroo.Payments.Infrastructure;

internal class StackConfig
{
    public required string AlarmTopicArn { get; init; }
    public required string ArtifactsBucketArn { get; init; }
    public required string Environment { get; init; }
    public required string LambdaArtifactVersion { get; init; }

    // VPC the recorder lambda runs in so it can resolve internal hostnames like
    // orders.luci-{env}. Mirrors Wayroo.Notification.Infrastructure's VPC wiring.
    public required string WayrooVpcId { get; init; }
    public required string[] WayrooAvailabilityZones { get; init; }
    public required string[] WayrooSubnetIds { get; init; }

    // ARN of the environment's webhook event bus (e.g. arn:aws:events:us-east-1:{acct}:event-bus/
    // {env}-webhook-bus). The bus is provisioned by another stack; we import it here so we can
    // attach a Rule that forwards events into the recorder lambda's source queue.
    public required string WebhookEventBusArn { get; init; }

    // ARN of the environment's intraprocess event bus (e.g. arn:aws:events:us-east-1:{acct}:
    // event-bus/{env}-wayroo-events). Also provisioned by another stack. This is the outbound half
    // of the pair: the recorder consumes from the webhook bus above and publishes store
    // configuration changes here. Passed to the lambda as an env var for the EventBridge publisher;
    // this stack attaches nothing to the bus.
    public required string WayrooEventsBusArn { get; init; }

    // ProPay/ProtectPay endpoints for this environment. These are the environment-wide DEFAULTS, the
    // same role PropayApiBaseUrisOptions plays in Luci.Orders. A tenant with a per-tenant base-URL
    // override in Parameter Store overrides them for that tenant alone; the defaults are not in
    // Parameter Store and are not expected to be.
    //
    // They arrive as stack parameters rather than being derived here because `Environment` is a
    // CfnParameter token at synth time — a C# branch on its value can never match, so each
    // environment has to supply its own literal through the pipeline variables.
    public required string PropayRestBaseUri { get; init; }
    public required string PropayXmlBaseUri { get; init; }
    public required string ProtectPayRestBaseUri { get; init; }

    // ECS Fargate API hosting parameters — used by the Wayroo.Payments.API construct, mirrors the
    // values Notification's stack consumes.
    public required string WayrooECSSecurityGroupId { get; init; }
    public required string CloudMapNamespaceId { get; init; }
    public required string CloudMapNamespaceArn { get; init; }
}
