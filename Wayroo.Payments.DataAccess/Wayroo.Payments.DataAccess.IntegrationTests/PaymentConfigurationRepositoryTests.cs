using AwesomeAssertions;
using Wayroo.Payments.DataAccess.IntegrationTests.Fixtures;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.DataAccess.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="PaymentConfigurationRepository"/> against a local DynamoDB instance.
/// Each test provisions its own table via the fixture for isolation. Mirrors NotificationRepositoryTests.
/// </summary>
[Collection(nameof(TestCollection))]
public class PaymentConfigurationRepositoryTests(TestFixture fixture)
{
    private static PaymentProviderConfiguration NewConfiguration(
        long storeId,
        string providerId = "propay",
        string? accountId = null) => new()
    {
        StoreId = storeId,
        ProviderId = providerId,
        AccountId = accountId ?? $"acct-{Guid.NewGuid()}",
        TenantId = 42,
        ProviderConfiguration = "{\"accountNum\":\"718040110898\",\"merchantId\":\"290031234BK1765\"}",
    };

    private static long NewStoreId() => Random.Shared.NextInt64(1, long.MaxValue);

    [Fact]
    public async Task UpsertConfiguration_ThenGetConfiguration_RoundTrips()
    {
        // Given
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);
        var configuration = NewConfiguration(NewStoreId());

        // When
        var upserted = await repo.UpsertConfiguration(configuration, CancellationToken.None);
        var retrieved = await repo.GetConfiguration(configuration.StoreId, configuration.ProviderId, CancellationToken.None);

        // Then
        retrieved.Should().NotBeNull();
        retrieved.Should().BeEquivalentTo(upserted);
    }

    [Fact]
    public async Task GetConfiguration_ReturnsNull_WhenMissing()
    {
        // Given
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);

        // When
        var retrieved = await repo.GetConfiguration(NewStoreId(), "propay", CancellationToken.None);

        // Then
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task UpsertConfiguration_OverwritesExisting_ForSameStoreAndProvider()
    {
        // Given
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);
        var storeId = NewStoreId();

        var original = NewConfiguration(storeId);
        original.ProviderConfiguration = "{\"version\":1}";
        await repo.UpsertConfiguration(original, CancellationToken.None);

        // When the same store + provider is written again
        var updated = NewConfiguration(storeId, original.ProviderId, original.AccountId);
        updated.ProviderConfiguration = "{\"version\":2}";
        await repo.UpsertConfiguration(updated, CancellationToken.None);

        // Then there is still one record, carrying the latest credential payload. Merging is per
        // attribute, so a writer still replaces the attributes it owns.
        var all = await repo.GetConfigurationsForStore(storeId, CancellationToken.None);
        all.Should().ContainSingle();
        all[0].ProviderConfiguration.Should().Be("{\"version\":2}");
    }

    [Fact]
    public async Task GetConfigurationsForStore_ReturnsEveryProviderForTheStore_Only()
    {
        // Given
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);
        var storeId = NewStoreId();
        var otherStoreId = NewStoreId();

        await repo.UpsertConfiguration(NewConfiguration(storeId, "propay"), CancellationToken.None);
        await repo.UpsertConfiguration(NewConfiguration(storeId, "stripe"), CancellationToken.None);
        await repo.UpsertConfiguration(NewConfiguration(storeId, "square"), CancellationToken.None);
        await repo.UpsertConfiguration(NewConfiguration(otherStoreId, "propay"), CancellationToken.None);

        // When
        var configurations = await repo.GetConfigurationsForStore(storeId, CancellationToken.None);

        // Then
        configurations.Should().HaveCount(3);
        configurations.Select(c => c.ProviderId).Should().BeEquivalentTo("propay", "stripe", "square");
        configurations.Should().OnlyContain(c => c.StoreId == storeId);
    }

    [Fact]
    public async Task GetConfigurationsForStore_ReturnsEmpty_WhenNoneExist()
    {
        // Given
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);

        // When
        var configurations = await repo.GetConfigurationsForStore(NewStoreId(), CancellationToken.None);

        // Then
        configurations.Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertConfiguration_StampsCreatedOnAndModifiedOn()
    {
        // Given
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);

        // When
        var upserted = await repo.UpsertConfiguration(NewConfiguration(NewStoreId()), CancellationToken.None);

        // Then
        upserted.CreatedOn.Should().NotBeNull();
        upserted.ModifiedOn.Should().NotBeNull();
        upserted.CreatedOn.Should().Be(upserted.ModifiedOn);
    }

    private static PaymentProviderConfiguration NewAccountDetails(
        long storeId,
        string providerId = "propay",
        string? accountId = null,
        PaymentAccountStatus status = PaymentAccountStatus.ReadyToProcess) => new()
    {
        StoreId = storeId,
        ProviderId = providerId,
        AccountId = accountId ?? $"acct-{Guid.NewGuid()}",
        TenantId = 42,
        ProviderAccountDetails = "{\"accountStatus\":\"ReadyToProcess\",\"tier\":\"PPZ_NCR\"}",
        AccountStatus = status,
    };

    [Fact]
    public async Task UpsertAccountDetails_ThenGetConfiguration_RoundTrips()
    {
        // Given
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);
        var details = NewAccountDetails(NewStoreId());

        // When
        await repo.UpsertAccountDetails(details, CancellationToken.None);
        var retrieved = await repo.GetConfiguration(details.StoreId, details.ProviderId, CancellationToken.None);

        // Then
        retrieved.Should().NotBeNull();
        retrieved!.AccountId.Should().Be(details.AccountId);
        retrieved.TenantId.Should().Be(details.TenantId);
        retrieved.ProviderAccountDetails.Should().Be(details.ProviderAccountDetails);
        retrieved.AccountStatus.Should().Be(PaymentAccountStatus.ReadyToProcess);
        retrieved.AccountDetailsRefreshedOn.Should().NotBeNull();
    }

    /// <summary>
    /// The reason both writers use UpdateItem instead of PutItem. The credential webhook and the
    /// account refresh land on the same record from different callers, at unpredictable times; a
    /// whole-item put by either would silently discard the other's work.
    /// </summary>
    [Fact]
    public async Task UpsertConfiguration_DoesNotEraseRecordedAccountDetails()
    {
        // Given an account refresh has recorded the store's standing
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);
        var storeId = NewStoreId();

        var details = NewAccountDetails(storeId, status: PaymentAccountStatus.Suspended);
        await repo.UpsertAccountDetails(details, CancellationToken.None);

        // When a credential webhook arrives afterwards
        var configuration = NewConfiguration(storeId, details.ProviderId, details.AccountId);
        configuration.ProviderConfiguration = "{\"version\":2}";
        await repo.UpsertConfiguration(configuration, CancellationToken.None);

        // Then the account information survives alongside the new credentials
        var retrieved = await repo.GetConfiguration(storeId, details.ProviderId, CancellationToken.None);
        retrieved!.ProviderConfiguration.Should().Be("{\"version\":2}");
        retrieved.ProviderAccountDetails.Should().Be(details.ProviderAccountDetails);
        retrieved.AccountStatus.Should().Be(PaymentAccountStatus.Suspended);
        retrieved.AccountDetailsRefreshedOn.Should().NotBeNull();
    }

    /// <summary>The same guarantee in the other direction.</summary>
    [Fact]
    public async Task UpsertAccountDetails_DoesNotEraseTheCredentialPayload()
    {
        // Given the recorder has stored the provider credentials
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);
        var storeId = NewStoreId();

        var configuration = NewConfiguration(storeId);
        configuration.ProviderConfiguration = "{\"tapToPay\":{\"terminalId\":\"76539084\"}}";
        await repo.UpsertConfiguration(configuration, CancellationToken.None);

        // When an account refresh runs afterwards
        await repo.UpsertAccountDetails(
            NewAccountDetails(storeId, configuration.ProviderId, configuration.AccountId),
            CancellationToken.None);

        // Then the credentials survive — losing these would break Tap to Pay for the store
        var retrieved = await repo.GetConfiguration(storeId, configuration.ProviderId, CancellationToken.None);
        retrieved!.ProviderConfiguration.Should().Be("{\"tapToPay\":{\"terminalId\":\"76539084\"}}");
        retrieved.AccountStatus.Should().Be(PaymentAccountStatus.ReadyToProcess);
    }

    [Fact]
    public async Task Writes_PreserveTheOriginalCreatedOn()
    {
        // Given
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);
        var storeId = NewStoreId();

        var first = await repo.UpsertConfiguration(NewConfiguration(storeId), CancellationToken.None);

        // When a later write lands on the same record
        await repo.UpsertAccountDetails(NewAccountDetails(storeId), CancellationToken.None);

        // Then CreatedOn still records when the record first appeared, and ModifiedOn has moved on
        var retrieved = await repo.GetConfiguration(storeId, "propay", CancellationToken.None);
        retrieved!.CreatedOn.Should().Be(first.CreatedOn);
        retrieved.ModifiedOn.Should().BeOnOrAfter(first.ModifiedOn!.Value);
    }

    [Fact]
    public async Task UpsertAccountDetails_CreatesTheRecord_WhenNoCredentialsHaveBeenRecorded()
    {
        // The ordinary backfill case: a store whose credential webhook never arrived.
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);
        var storeId = NewStoreId();

        await repo.UpsertAccountDetails(NewAccountDetails(storeId), CancellationToken.None);

        var retrieved = await repo.GetConfiguration(storeId, "propay", CancellationToken.None);
        retrieved.Should().NotBeNull();
        retrieved!.ProviderConfiguration.Should().BeNull();
        retrieved.AccountStatus.Should().Be(PaymentAccountStatus.ReadyToProcess);
        retrieved.CreatedOn.Should().NotBeNull();
    }

    private static StoreRoutingConfiguration NewRouting(
        long storeId,
        string acquiringProviderId = "propay",
        string? migrationState = null) => new()
    {
        StoreId = storeId,
        TenantId = 42,
        AcquiringProviderId = acquiringProviderId,
        MigrationState = migrationState ?? MigrationStates.PropayActive,
    };

    [Fact]
    public async Task UpsertRouting_ThenGetRouting_RoundTrips()
    {
        // Given
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);
        var storeId = NewStoreId();

        // When
        var written = await repo.UpsertRouting(NewRouting(storeId), CancellationToken.None);
        var retrieved = await repo.GetRouting(storeId, CancellationToken.None);

        // Then
        written.Previous.Should().BeNull();
        written.Changed.Should().BeTrue("creating the record is itself news worth announcing");
        retrieved.Should().NotBeNull();
        retrieved!.AcquiringProviderId.Should().Be("propay");
        retrieved.MigrationState.Should().Be(MigrationStates.PropayActive);
        retrieved.ConfigurationVersion.Should().Be(1);
    }

    [Fact]
    public async Task GetRouting_ReturnsNull_WhenNothingHasBeenRecorded()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);

        // An absent record is what lets a store keep resolving to the platform default.
        var retrieved = await repo.GetRouting(NewStoreId(), CancellationToken.None);

        retrieved.Should().BeNull();
    }

    /// <summary>
    /// The version has to advance on every write, server-side. A repeated version reads as stale
    /// downstream, so a real change carrying one would be silently dropped by consumers.
    /// </summary>
    [Fact]
    public async Task UpsertRouting_IncrementsTheVersionOnEveryWrite()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);
        var storeId = NewStoreId();

        var first = await repo.UpsertRouting(NewRouting(storeId), CancellationToken.None);
        var second = await repo.UpsertRouting(NewRouting(storeId), CancellationToken.None);
        var third = await repo.UpsertRouting(NewRouting(storeId, "adyen"), CancellationToken.None);

        first.Current.ConfigurationVersion.Should().Be(1);
        second.Current.ConfigurationVersion.Should().Be(2);
        third.Current.ConfigurationVersion.Should().Be(3);

        // Rewriting the same values is not a change; moving the provider is.
        second.Changed.Should().BeFalse();
        third.Changed.Should().BeTrue();
        third.Previous!.AcquiringProviderId.Should().Be("propay");
    }

    /// <summary>
    /// Routing lives on its own item in the store's partition, so writing it must not disturb the
    /// credential payload or the recorded account information — and must not be handed back as if it
    /// were a provider configuration.
    /// </summary>
    [Fact]
    public async Task UpsertRouting_DisturbsNeitherTheCredentialsNorTheAccountDetails()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);
        var storeId = NewStoreId();

        var configuration = NewConfiguration(storeId);
        configuration.ProviderConfiguration = "{\"tapToPay\":{\"terminalId\":\"76539084\"}}";
        await repo.UpsertConfiguration(configuration, CancellationToken.None);
        await repo.UpsertAccountDetails(
            NewAccountDetails(storeId, configuration.ProviderId, configuration.AccountId),
            CancellationToken.None);

        await repo.UpsertRouting(NewRouting(storeId, "adyen"), CancellationToken.None);

        var retrieved = await repo.GetConfiguration(storeId, configuration.ProviderId, CancellationToken.None);
        retrieved!.ProviderConfiguration.Should().Be("{\"tapToPay\":{\"terminalId\":\"76539084\"}}");
        retrieved.ProviderAccountDetails.Should().NotBeNull();
        retrieved.AccountStatus.Should().Be(PaymentAccountStatus.ReadyToProcess);
    }

    [Fact]
    public async Task GetConfigurationsForStore_DoesNotReturnTheRoutingRecord()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);
        var storeId = NewStoreId();

        await repo.UpsertConfiguration(NewConfiguration(storeId, "propay"), CancellationToken.None);
        await repo.UpsertRouting(NewRouting(storeId), CancellationToken.None);

        var configurations = await repo.GetConfigurationsForStore(storeId, CancellationToken.None);

        // The routing item shares the partition; listing a store's providers must skip it.
        configurations.Should().ContainSingle();
        configurations[0].ProviderId.Should().Be("propay");
        configurations.Should().NotContain(c =>
            c.ProviderId == PaymentConfigurationSchemaProvider.RoutingSortKey);
    }

    [Fact]
    public async Task GetConfiguration_ReadsBackTheRecordedAccountStatus()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetRepository(client, options);
        var configuration = NewConfiguration(NewStoreId());

        await repo.UpsertAccountDetails(
            NewAccountDetails(configuration.StoreId, configuration.ProviderId, configuration.AccountId),
            CancellationToken.None);

        var retrieved = await repo.GetConfiguration(configuration.StoreId, configuration.ProviderId, CancellationToken.None);
        retrieved!.AccountStatus.Should().Be(PaymentAccountStatus.ReadyToProcess);
    }
}
