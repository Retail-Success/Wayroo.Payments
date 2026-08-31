using System.ComponentModel.DataAnnotations;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.DataAccess;

/// <summary>
/// Settings for this library's DynamoDB access: which table, which region, and an optional local
/// endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Bound from configuration in two passes — see <c>AddPaymentsDataAccess</c>. The table name and
/// region come from the configuration <b>root</b>, so their property names are the key names
/// (<c>PaymentConfigurationTableName</c>, <c>AwsRegion</c>); <see cref="AwsClientOptions.ServiceUrl"/>
/// comes from the <see cref="SectionName"/> section, so it is <c>DynamoDb:ServiceUrl</c>. Renaming a
/// property here renames a deployed configuration key.
/// </para>
/// <para>
/// Settable rather than <c>init</c>-only properties: the options pipeline constructs the instance and
/// then binds onto it, and post-configuration would have nothing to write to otherwise.
/// </para>
/// </remarks>
public class DynamoDbClientOptions : AwsClientOptions
{
    /// <inheritdoc cref="DataAccessConfigurationKeys.DynamoDbSection" />
    public const string SectionName = DataAccessConfigurationKeys.DynamoDbSection;

    /// <summary>
    /// The unprefixed table name. Only correct for local development: every deployed environment
    /// supplies <c>{env}-PaymentConfiguration</c> instead.
    /// </summary>
    /// <remarks>
    /// <c>Wayroo.Payments.Infrastructure</c> reads this default off a <c>new</c> instance of this class
    /// to derive the table's name at synth time (see <c>ResourceStack</c>), which is what keeps the
    /// deployed name and the name this library looks for from drifting. Changing it changes the
    /// deployed table.
    /// </remarks>
    public const string DefaultPaymentConfigurationTableName = "PaymentConfiguration";

    /// <summary>
    /// Name of the DynamoDB table storing payment provider configurations.
    /// </summary>
    // Required, not merely defaulted: a deployed host that fails to pass the environment-prefixed
    // name would otherwise fall back to the bare default and quietly query a table that does not
    // exist, which surfaces as a ResourceNotFoundException on the first read rather than at startup.
    // Non-blank only — the lambda's integration tests set every key to junk to prove the function
    // constructs, so a DynamoDB naming-rules check here would fail them.
    [Required(ErrorMessage =
        $"{DataAccessConfigurationKeys.PaymentConfigurationTableName} must be supplied.")]
    public string PaymentConfigurationTableName { get; set; } = DefaultPaymentConfigurationTableName;
}
