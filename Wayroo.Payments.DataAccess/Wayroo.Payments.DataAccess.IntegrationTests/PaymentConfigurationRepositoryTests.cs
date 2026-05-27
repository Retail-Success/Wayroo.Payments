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

        // Then only the latest record exists
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
}
