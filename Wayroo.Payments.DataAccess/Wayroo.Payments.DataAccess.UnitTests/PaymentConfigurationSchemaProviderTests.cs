using AwesomeAssertions;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.DataAccess.UnitTests;

/// <summary>
/// Unit tests for <see cref="PaymentConfigurationSchemaProvider"/>: attribute naming, the
/// store/provider key shape, reading an item back into a model, and — the point of the design — the
/// fact that the two writers name disjoint sets of attributes so neither can erase the other's.
/// </summary>
public class PaymentConfigurationSchemaProviderTests
{
    private static readonly DateTimeOffset Now = new(2025, 4, 18, 11, 0, 0, TimeSpan.Zero);

    private static StoreRoutingConfiguration NewRouting() => new()
    {
        StoreId = 1007,
        TenantId = 42,
        AcquiringProviderId = "propay",
        MigrationState = MigrationStates.PropayActive,
    };

    private static PaymentProviderConfiguration NewConfiguration() => new()
    {
        StoreId = 1007,
        ProviderId = "propay",
        AccountId = "718040110898",
        TenantId = 42,
        ProviderConfiguration = "{\"accountNum\":\"718040110898\",\"merchantId\":\"290031234BK1765\"}",
        ProviderAccountDetails = "{\"accountStatus\":\"ReadyToProcess\"}",
        AccountStatus = PaymentAccountStatus.ReadyToProcess,
        AccountDetailsRefreshedOn = new DateTimeOffset(2025, 4, 18, 10, 0, 0, TimeSpan.Zero),
        CreatedOn = new DateTimeOffset(2025, 4, 17, 10, 37, 42, TimeSpan.Zero),
        ModifiedOn = Now,
    };

    [Fact]
    public void KeyAttributeNames_MatchSchema()
    {
        PaymentConfigurationSchemaProvider.AttributeNameForPartitionKey.Should().Be("StoreId");
        PaymentConfigurationSchemaProvider.AttributeNameForSortKey.Should().Be("ProviderId");
    }

    [Fact]
    public void GetRecordIdentifiers_ContainsOnlyTheKeys()
    {
        var keys = PaymentConfigurationSchemaProvider.GetRecordIdentifiers(1007, "propay");

        keys.Should().HaveCount(2).And.ContainKeys("StoreId", "ProviderId");
        keys["StoreId"].N.Should().Be("1007");
        keys["ProviderId"].S.Should().Be("propay");
    }

    [Fact]
    public void GetConfigurationUpdate_WritesOnlyTheCredentialAttributes()
    {
        var update = PaymentConfigurationSchemaProvider.GetConfigurationUpdate(NewConfiguration(), Now);

        update.ExpressionAttributeNames.Values.Should().BeEquivalentTo(
            "AccountId", "TenantId", "ProviderConfiguration", "CreatedOn", "ModifiedOn");
    }

    [Fact]
    public void GetAccountDetailsUpdate_WritesOnlyTheAccountAttributes()
    {
        var update = PaymentConfigurationSchemaProvider.GetAccountDetailsUpdate(NewConfiguration(), Now);

        update.ExpressionAttributeNames.Values.Should().BeEquivalentTo(
            "AccountId",
            "TenantId",
            "ProviderAccountDetails",
            "AccountStatus",
            "AccountDetailsRefreshedOn",
            "CreatedOn",
            "ModifiedOn");
    }

    [Fact]
    public void GetRoutingUpdate_WritesTheRoutingFields_AndIncrementsTheVersion()
    {
        var update = PaymentConfigurationSchemaProvider.GetRoutingUpdate(NewRouting(), Now);

        update.ExpressionAttributeNames.Values.Should().BeEquivalentTo(
            "TenantId", "AcquiringProviderId", "MigrationState", "ConfigurationVersion",
            "CreatedOn", "ModifiedOn");

        // ADD, not SET: two concurrent writers must not be able to mint the same version, because a
        // repeated version reads as stale downstream and the later change would be dropped.
        update.UpdateExpression.Should().Contain("ADD #ConfigurationVersion :versionIncrement");
        update.ExpressionAttributeValues[":versionIncrement"].N.Should().Be("1");
        update.ExpressionAttributeValues[":AcquiringProviderId"].S.Should().Be("propay");
    }

    [Fact]
    public void GetRoutingUpdate_DefaultsAnUnsetMigrationState()
    {
        var routing = NewRouting();
        routing.MigrationState = "   ";

        var update = PaymentConfigurationSchemaProvider.GetRoutingUpdate(routing, Now);

        update.ExpressionAttributeValues[":MigrationState"].S.Should().Be(MigrationStates.PropayActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void GetRoutingUpdate_Throws_WhenStoreIdNotPositive(long storeId)
    {
        var routing = NewRouting();
        routing.StoreId = storeId;

        var act = () => PaymentConfigurationSchemaProvider.GetRoutingUpdate(routing, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetRoutingUpdate_Throws_WhenAcquiringProviderMissing(string acquiringProviderId)
    {
        var routing = NewRouting();
        routing.AcquiringProviderId = acquiringProviderId;

        var act = () => PaymentConfigurationSchemaProvider.GetRoutingUpdate(routing, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RoutingIdentifiers_UseTheReservedSortKey()
    {
        var keys = PaymentConfigurationSchemaProvider.GetRoutingIdentifiers(1007);

        keys["StoreId"].N.Should().Be("1007");
        // Prefixed so it can never collide with a provider id.
        keys["ProviderId"].S.Should().Be(PaymentConfigurationSchemaProvider.RoutingSortKey);
        PaymentConfigurationSchemaProvider.RoutingSortKey.Should().StartWith("#");
    }

    [Fact]
    public void GetRoutingModel_RoundTripsTheRoutingUpdate()
    {
        var routing = NewRouting();
        var update = PaymentConfigurationSchemaProvider.GetRoutingUpdate(routing, Now);

        var item = PaymentConfigurationSchemaProvider.GetRoutingIdentifiers(routing.StoreId);
        foreach (var attributeName in update.ExpressionAttributeNames.Values)
        {
            if (update.ExpressionAttributeValues.TryGetValue($":{attributeName}", out var value))
                item[attributeName] = value;
        }

        var model = PaymentConfigurationSchemaProvider.GetRoutingModel(item);

        model.StoreId.Should().Be(routing.StoreId);
        model.TenantId.Should().Be(routing.TenantId);
        model.AcquiringProviderId.Should().Be(routing.AcquiringProviderId);
        model.MigrationState.Should().Be(routing.MigrationState);
    }

    [Fact]
    public void GetRoutingModel_DefaultsAnAbsentMigrationState()
    {
        var item = PaymentConfigurationSchemaProvider.GetRoutingIdentifiers(1007);

        PaymentConfigurationSchemaProvider.GetRoutingModel(item)
            .MigrationState.Should().Be(MigrationStates.PropayActive);
    }

    /// <summary>
    /// The guarantee the whole split exists for: a credential webhook must not name the account
    /// attributes, and an account refresh must not name the credential payload. If either writer
    /// grows into the other's territory, the attribute it does not own gets erased on every write.
    /// </summary>
    [Fact]
    public void TheTwoWriters_DoNotNameEachOthersAttributes()
    {
        var configuration = NewConfiguration();

        var credentialWrite = PaymentConfigurationSchemaProvider.GetConfigurationUpdate(configuration, Now);
        var accountWrite = PaymentConfigurationSchemaProvider.GetAccountDetailsUpdate(configuration, Now);

        credentialWrite.ExpressionAttributeNames.Values.Should().NotContain(
            ["ProviderAccountDetails", "AccountStatus", "AccountDetailsRefreshedOn"]);
        accountWrite.ExpressionAttributeNames.Values.Should().NotContain("ProviderConfiguration");

        // Routing lives on its own item, so it shares no attribute with either provider-row writer.
        var routingWrite = PaymentConfigurationSchemaProvider.GetRoutingUpdate(NewRouting(), Now);
        routingWrite.ExpressionAttributeNames.Values.Should().NotContain(
            ["ProviderConfiguration", "ProviderAccountDetails", "AccountStatus", "AccountDetailsRefreshedOn"]);
    }

    [Fact]
    public void Updates_SetModifiedOn_AndOnlySeedCreatedOnOnInsert()
    {
        var update = PaymentConfigurationSchemaProvider.GetConfigurationUpdate(NewConfiguration(), Now);

        update.UpdateExpression.Should().StartWith("SET ");
        update.UpdateExpression.Should().Contain("#CreatedOn = if_not_exists(#CreatedOn, :ModifiedOn)");
        update.UpdateExpression.Should().Contain("#ModifiedOn = :ModifiedOn");
        update.ExpressionAttributeValues[":ModifiedOn"].S.Should().Be(Now.ToString("O"));
    }

    [Fact]
    public void Updates_BindEveryAttributeThroughAPlaceholder()
    {
        var update = PaymentConfigurationSchemaProvider.GetAccountDetailsUpdate(NewConfiguration(), Now);

        // Names bound through #placeholders is what keeps a reserved word from breaking the
        // expression; every placeholder the expression mentions must therefore be declared.
        foreach (var placeholder in update.ExpressionAttributeNames.Keys)
            update.UpdateExpression.Should().Contain(placeholder);

        foreach (var placeholder in update.ExpressionAttributeValues.Keys)
            update.UpdateExpression.Should().Contain(placeholder);
    }

    [Fact]
    public void GetConfigurationUpdate_StoresStoreIdAsNumber_AndProviderIdAsString()
    {
        var update = PaymentConfigurationSchemaProvider.GetConfigurationUpdate(NewConfiguration(), Now);

        // The keys are not part of the update expression — they travel as the item key — so what is
        // asserted here is the value encoding of the non-key attributes.
        update.ExpressionAttributeValues[":TenantId"].N.Should().Be("42");
        update.ExpressionAttributeValues[":AccountId"].S.Should().Be("718040110898");
    }

    [Fact]
    public void GetAccountDetailsUpdate_StoresStatusByName()
    {
        var update = PaymentConfigurationSchemaProvider.GetAccountDetailsUpdate(NewConfiguration(), Now);

        update.ExpressionAttributeValues[":AccountStatus"].S.Should().Be("ReadyToProcess");
    }

    [Fact]
    public void GetAccountDetailsUpdate_StampsRefreshedOn_WhenTheCallerLeftItUnset()
    {
        var configuration = NewConfiguration();
        configuration.AccountDetailsRefreshedOn = null;

        var update = PaymentConfigurationSchemaProvider.GetAccountDetailsUpdate(configuration, Now);

        update.ExpressionAttributeValues[":AccountDetailsRefreshedOn"].S.Should().Be(Now.ToString("O"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Updates_OmitAccountId_WhenMissing(string? accountId)
    {
        var configuration = NewConfiguration();
        configuration.AccountId = accountId;

        var credentialWrite = PaymentConfigurationSchemaProvider.GetConfigurationUpdate(configuration, Now);
        var accountWrite = PaymentConfigurationSchemaProvider.GetAccountDetailsUpdate(configuration, Now);

        // Sparse rather than nulled: a writer that does not know the account id must not wipe one
        // another writer already established.
        credentialWrite.ExpressionAttributeValues.Should().NotContainKey(":AccountId");
        accountWrite.ExpressionAttributeValues.Should().NotContainKey(":AccountId");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Updates_Throw_WhenStoreIdNotPositive(long storeId)
    {
        var configuration = NewConfiguration();
        configuration.StoreId = storeId;

        var configurationWrite = () => PaymentConfigurationSchemaProvider.GetConfigurationUpdate(configuration, Now);
        var accountWrite = () => PaymentConfigurationSchemaProvider.GetAccountDetailsUpdate(configuration, Now);

        configurationWrite.Should().Throw<ArgumentException>();
        accountWrite.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Updates_Throw_WhenProviderIdMissing(string providerId)
    {
        var configuration = NewConfiguration();
        configuration.ProviderId = providerId;

        var configurationWrite = () => PaymentConfigurationSchemaProvider.GetConfigurationUpdate(configuration, Now);
        var accountWrite = () => PaymentConfigurationSchemaProvider.GetAccountDetailsUpdate(configuration, Now);

        configurationWrite.Should().Throw<ArgumentException>();
        accountWrite.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetModel_ReadsEveryAttribute()
    {
        var configuration = NewConfiguration();
        var item = PaymentConfigurationSchemaProvider.GetRecordIdentifiers(
            configuration.StoreId,
            configuration.ProviderId);

        // Merge both writers' output into one item, which is what the table actually holds once a
        // store has been through the recorder and a refresh.
        foreach (var write in new[]
                 {
                     PaymentConfigurationSchemaProvider.GetConfigurationUpdate(configuration, Now),
                     PaymentConfigurationSchemaProvider.GetAccountDetailsUpdate(configuration, Now),
                 })
        {
            foreach (var attributeName in write.ExpressionAttributeNames.Values)
            {
                // CreatedOn is assigned from :ModifiedOn through if_not_exists, so it has no value
                // placeholder of its own and is simply not part of this reconstruction.
                if (write.ExpressionAttributeValues.TryGetValue($":{attributeName}", out var value))
                    item[attributeName] = value;
            }
        }

        var model = PaymentConfigurationSchemaProvider.GetModel(item);

        model.StoreId.Should().Be(configuration.StoreId);
        model.ProviderId.Should().Be(configuration.ProviderId);
        model.AccountId.Should().Be(configuration.AccountId);
        model.TenantId.Should().Be(configuration.TenantId);
        model.ProviderConfiguration.Should().Be(configuration.ProviderConfiguration);
        model.ProviderAccountDetails.Should().Be(configuration.ProviderAccountDetails);
        model.AccountStatus.Should().Be(configuration.AccountStatus);
        model.AccountDetailsRefreshedOn.Should().Be(configuration.AccountDetailsRefreshedOn);
        model.ModifiedOn.Should().Be(Now);
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
        model.ProviderAccountDetails.Should().BeNull();
        model.AccountStatus.Should().BeNull();
        model.AccountDetailsRefreshedOn.Should().BeNull();
        model.CreatedOn.Should().BeNull();
        model.ModifiedOn.Should().BeNull();
    }

    [Fact]
    public void GetModel_ReadsAnUnrecognisedStatusAsNull()
    {
        var record = PaymentConfigurationSchemaProvider.GetRecordIdentifiers(1007, "propay");
        record["AccountStatus"] = new Amazon.DynamoDBv2.Model.AttributeValue { S = "SomethingNewFromTheProvider" };

        // A row written by a newer deployment must not poison an older one.
        PaymentConfigurationSchemaProvider.GetModel(record).AccountStatus.Should().BeNull();
    }
}
