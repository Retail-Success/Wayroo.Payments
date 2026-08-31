namespace Wayroo.Payments.Models;

/// <summary>
/// The AWS client settings every tier of this service needs: which region to talk to, and an
/// optional endpoint override for pointing at a local emulator.
/// </summary>
/// <remarks>
/// <para>
/// A base class rather than an options type of its own on purpose. Both the data access and eventing
/// tiers register into the same container in the recorder lambda, so a single shared
/// <c>IOptions&lt;AwsClientOptions&gt;</c> would have <c>DynamoDb:ServiceUrl</c> and
/// <c>EventBridge:ServiceUrl</c> overwrite each other depending on which tier registered last.
/// Inheriting gives each tier a distinct options type — so plain <c>IOptions&lt;T&gt;</c> keeps
/// working — while the region property, its default and this explanation live in one place.
/// </para>
/// <para>
/// Lives here rather than in a tier because <see cref="Wayroo.Payments.Models"/> is the only project
/// both tiers can see, and these are plain properties: no package reference is needed to declare
/// them, so the project's deliberate zero-dependency rule survives.
/// </para>
/// <para>
/// <b>These are bound from the configuration root, so a property name here IS a configuration key,
/// in every tier that inherits.</b> Adding a property called <c>Region</c> or <c>Endpoint</c> would
/// silently start picking up a root key of that name from any configuration source — which, in the
/// API, includes the flattened ProPay credential tree from Parameter Store. Add one only when the
/// key name is what you want.
/// </para>
/// </remarks>
public abstract class AwsClientOptions
{
    /// <summary>
    /// The region every AWS client in the tier targets. Only load-bearing for local development and
    /// tests: the CDK sets <c>AwsRegion</c> explicitly for both the API task definition and the
    /// recorder lambda.
    /// </summary>
    public const string DefaultAwsRegion = "us-east-1";

    /// <inheritdoc cref="DefaultAwsRegion" />
    public string AwsRegion { get; set; } = DefaultAwsRegion;

    /// <summary>
    /// Points the tier's AWS client at something other than AWS — DynamoDB Local, or a stub standing
    /// in for a service with no emulator. Null everywhere but tests, where it is the only way to
    /// exercise a code path without reaching a real account.
    /// </summary>
    /// <remarks>
    /// Set <b>after</b> the region on the SDK's client config, never in the same object initializer:
    /// <c>RegionEndpoint</c> and <c>ServiceURL</c> are mutually exclusive there and each clears the
    /// other.
    /// </remarks>
    public string? ServiceUrl { get; set; }
}
