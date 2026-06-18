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

    // ECS Fargate API hosting parameters — used by the Wayroo.Payments.API construct, mirrors the
    // values Notification's stack consumes.
    public required string WayrooECSSecurityGroupId { get; init; }
    public required string CloudMapNamespaceId { get; init; }
    public required string CloudMapNamespaceArn { get; init; }
}
