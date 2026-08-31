using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wayroo.Payments.DataAccess.Extensions;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.DataAccess.UnitTests;

/// <summary>
/// Pins how <see cref="DynamoDbClientOptions"/> is bound from configuration.
/// </summary>
/// <remarks>
/// <para>
/// Worth its own file because getting this wrong is not a compile error and not a test failure
/// anywhere else — it is a <c>ResourceNotFoundException</c> on the first read against a table that
/// does not exist, from a service that started up reporting itself healthy.
/// </para>
/// <para>
/// The settings deliberately sit at two different depths (the table name and region at the
/// configuration root, because that is how the CDK passes them as container and lambda environment
/// variables; <c>ServiceUrl</c> under the <c>DynamoDb</c> section), so the options are bound in two
/// passes. The failure mode these tests exist for is the second pass <i>replacing</i> the first
/// instead of overlaying it, which would silently reset the table name to its local default.
/// </para>
/// </remarks>
public class DynamoDbClientOptionsBindingTests
{
    private static DynamoDbClientOptions Bind(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair =>
                new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddPaymentsDataAccess(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<DynamoDbClientOptions>>().Value;
    }

    [Fact]
    public void BindsTheTableNameAndRegionFromTheConfigurationRoot()
    {
        var options = Bind(
            (DataAccessConfigurationKeys.PaymentConfigurationTableName, "dev-PaymentConfiguration"),
            (DataAccessConfigurationKeys.AwsRegion, "us-west-2"));

        options.PaymentConfigurationTableName.Should().Be("dev-PaymentConfiguration");
        options.AwsRegion.Should().Be("us-west-2");
        options.ServiceUrl.Should().BeNull();
    }

    [Fact]
    public void KeepsTheRootValues_WhenTheSectionOverlayAlsoApplies()
    {
        // The regression this file exists for: two binds, and the second must not wipe what the first
        // established.
        var options = Bind(
            (DataAccessConfigurationKeys.PaymentConfigurationTableName, "qa-PaymentConfiguration"),
            (DataAccessConfigurationKeys.DynamoDbServiceUrl, "http://localhost:8000"));

        options.PaymentConfigurationTableName.Should().Be("qa-PaymentConfiguration");
        options.ServiceUrl.Should().Be("http://localhost:8000");
    }

    [Fact]
    public void FallsBackToTheLocalDefaults_WhenNothingIsConfigured()
    {
        var options = Bind();

        options.PaymentConfigurationTableName
            .Should().Be(DynamoDbClientOptions.DefaultPaymentConfigurationTableName);
        options.AwsRegion.Should().Be(AwsClientOptions.DefaultAwsRegion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FailsStartupValidation_WhenTheTableNameIsBlank(string tableName)
    {
        // A present-but-empty environment variable is the realistic failure — a task definition or
        // lambda environment entry that resolved to nothing. Binding writes it through, so without the
        // [Required] this would fall back to the bare local default and query the wrong table.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>(
                    DataAccessConfigurationKeys.PaymentConfigurationTableName, tableName),
            ])
            .Build();

        var services = new ServiceCollection();
        services.AddPaymentsDataAccess(configuration);
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{DataAccessConfigurationKeys.PaymentConfigurationTableName}*");
    }
}
