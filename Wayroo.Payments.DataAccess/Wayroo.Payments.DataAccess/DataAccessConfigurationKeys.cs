namespace Wayroo.Payments.DataAccess;

/// <summary>
/// Configuration keys this library reads. Hosts surface them however they like — env vars on the
/// API's ECS task definition and on the recorder lambda today — and the names are the contract.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately plain constants with <b>no <c>Keys()</c> method</b>, unlike the two
/// <c>EnvironmentVariableKeys</c> classes in the hosts. Those are harvested by reflection, so every
/// public static string field on them becomes a hard startup requirement; that decision belongs to
/// the host, which knows which of these it actually needs, not to this library.
/// </para>
/// <para>
/// These exist mostly so the options class and the CDK have something to point at. What the binder
/// actually matches on is the <i>property names</i> of
/// <see cref="DynamoDbClientOptions"/> — see the note there.
/// </para>
/// </remarks>
public static class DataAccessConfigurationKeys
{
    /// <summary>AWS region the DynamoDB client targets. Shared with the eventing and SQS wiring.</summary>
    public const string AwsRegion = nameof(AwsRegion);

    /// <summary>
    /// Name of the DynamoDB table holding payment provider configurations. The deployed value is
    /// environment-prefixed (<c>{env}-PaymentConfiguration</c>) and comes from the CDK; the
    /// unprefixed default only ever applies locally.
    /// </summary>
    public const string PaymentConfigurationTableName = nameof(PaymentConfigurationTableName);

    /// <summary>
    /// Configuration section carrying an optional <c>ServiceUrl</c> that points the DynamoDB client at
    /// DynamoDB Local instead of AWS. Unset everywhere but tests.
    /// </summary>
    public const string DynamoDbSection = "DynamoDb";

    /// <inheritdoc cref="DynamoDbSection" />
    public const string DynamoDbServiceUrl = $"{DynamoDbSection}:ServiceUrl";
}
