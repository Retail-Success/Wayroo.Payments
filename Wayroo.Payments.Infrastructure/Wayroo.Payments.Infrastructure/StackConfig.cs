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
}
