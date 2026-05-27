using AwesomeAssertions;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.DataAccess.UnitTests;

/// <summary>
/// Unit tests for <see cref="PaymentConfigurationSchemaProvider"/>: attribute naming, the store/provider
/// key shape, the sparse account-id attribute, and round-tripping a model through the record form.
/// </summary>
public class PaymentConfigurationSchemaProviderTests
{
    private static PaymentProviderConfiguration NewConfiguration() => new()
    {
        StoreId = 1007,
        ProviderId = "propay",
        AccountId = "718040110898",
        TenantId = 42,
        ProviderConfiguration = "{\"accountNum\":\"718040110898\",\"merchantId\":\"290031234BK1765\"}",
        CreatedOn = new DateTimeOffset(2025, 4, 17, 10, 37, 42, TimeSpan.Zero),
        ModifiedOn = new DateTimeOffset(2025, 4, 18, 11, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void KeyAttributeNames_MatchSchema()
    {
        PaymentConfigurationSchemaProvider.AttributeNameForPartitionKey.Should().Be("StoreId");
        PaymentConfigurationSchemaProvider.AttributeNameForSortKey.Should().Be("ProviderId");
    }

    [Fact]
    public void GetRecord_RoundTripsThroughGetModel()
    {
        var configuration = NewConfiguration();

        var record = PaymentConfigurationSchemaProvider.GetRecord(configuration);
        var roundTripped = PaymentConfigurationSchemaProvider.GetModel(record);

        roundTripped.Should().BeEquivalentTo(configuration);
    }

    [Fact]
    public void GetRecord_StoresStoreIdAsNumber_AndProviderIdAsString()
    {
        var record = PaymentConfigurationSchemaProvider.GetRecord(NewConfiguration());

        record["StoreId"].N.Should().Be("1007");
        record["ProviderId"].S.Should().Be("propay");
        record["TenantId"].N.Should().Be("42");
    }

    [Fact]
    public void GetRecord_WritesAccountId_WhenPresent()
    {
        var record = PaymentConfigurationSchemaProvider.GetRecord(NewConfiguration());

        record.Should().ContainKey("AccountId");
        record["AccountId"].S.Should().Be("718040110898");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetRecord_OmitsAccountId_WhenMissing(string? accountId)
    {
        var configuration = NewConfiguration();
        configuration.AccountId = accountId;

        var record = PaymentConfigurationSchemaProvider.GetRecord(configuration);

        record.Should().NotContainKey("AccountId");
    }

    [Fact]
    public void GetRecordIdentifiers_ContainsOnlyTheKeys()
    {
        var keys = PaymentConfigurationSchemaProvider.GetRecordIdentifiers(1007, "propay");

        keys.Should().HaveCount(2).And.ContainKeys("StoreId", "ProviderId");
        keys["StoreId"].N.Should().Be("1007");
        keys["ProviderId"].S.Should().Be("propay");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void GetRecord_Throws_WhenStoreIdNotPositive(long storeId)
    {
        var configuration = NewConfiguration();
        configuration.StoreId = storeId;

        var act = () => PaymentConfigurationSchemaProvider.GetRecord(configuration);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetRecord_Throws_WhenProviderIdMissing(string providerId)
    {
        var configuration = NewConfiguration();
        configuration.ProviderId = providerId;

        var act = () => PaymentConfigurationSchemaProvider.GetRecord(configuration);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetModel_LeavesOptionalAttributesNull_WhenAbsent()
    {
        // Only the required keys are present on the item.
        var record = PaymentConfigurationSchemaProvider.GetRecordIdentifiers(1007, "propay");

        var model = PaymentConfigurationSchemaProvider.GetModel(record);

        model.StoreId.Should().Be(1007);
        model.ProviderId.Should().Be("propay");
        model.AccountId.Should().BeNull();
        model.TenantId.Should().BeNull();
        model.ProviderConfiguration.Should().BeNull();
        model.CreatedOn.Should().BeNull();
        model.ModifiedOn.Should().BeNull();
    }
}
