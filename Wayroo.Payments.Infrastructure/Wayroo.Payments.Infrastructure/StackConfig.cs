namespace Wayroo.Payments.Infrastructure;

internal class StackConfig
{
    public required string AlarmTopicArn { get; init; }
    public required string ArtifactsBucketArn { get; init; }
    public required string Environment { get; init; }
    public required string LambdaArtifactVersion { get; init; }
    public required string OrdersApiBaseUrl { get; init; }
}
