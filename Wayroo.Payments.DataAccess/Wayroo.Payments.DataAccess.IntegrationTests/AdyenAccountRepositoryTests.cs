using AwesomeAssertions;
using Wayroo.Payments.DataAccess.IntegrationTests.Fixtures;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.DataAccess.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="AdyenAccountRepository"/> against a local DynamoDB instance.
/// </summary>
/// <remarks>
/// The behaviours pinned here are the ones the onboarding ladder depends on and that a unit test
/// cannot prove: that an identifier is written once and only once, that the version advances on the
/// database rather than in the caller, that a grace deadline genuinely disappears when it ends, and
/// that these writes leave the other writers' attributes on the shared record alone.
/// </remarks>
[Collection(nameof(TestCollection))]
public class AdyenAccountRepositoryTests(TestFixture fixture)
{
    private static long NewStoreId() => Random.Shared.NextInt64(1, long.MaxValue);

    // A person identifier the platform does not mint yet, so that the round trip is exercised rather
    // than only the absent case.
    private static readonly Guid OwnerId = Guid.ParseExact("8f14e45fceea167a5a36dedd4bea2543", "N");

    private static AdyenAccount NewAccount(long storeId) => new()
    {
        StoreId = storeId,
        TenantId = 4,
        OwnerId = OwnerId,
    };

    private static AdyenCapabilityState Usable(long sequence = 1) => new()
    {
        Requested = true,
        Enabled = true,
        Allowed = true,
        VerificationStatus = "valid",
        LastEventSequence = sequence,
    };

    [Fact]
    public async Task GetAdyenAccount_ReturnsNull_WhenOnboardingHasNeverRun()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetAdyenRepository(client, options);

        var retrieved = await repo.GetAdyenAccount(NewStoreId(), CancellationToken.None);

        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task RecordOnboardingProgress_WritesOneRungAtATime_AndAdvancesTheStep()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetAdyenRepository(client, options);
        var storeId = NewStoreId();

        var afterLegalEntity = NewAccount(storeId);
        afterLegalEntity.LegalEntityId = "LE00000000000000000000001";
        afterLegalEntity.OnboardingStep = AdyenOnboardingStep.LegalEntityCreated;
        await repo.RecordOnboardingProgress(afterLegalEntity, CancellationToken.None);

        var afterBusinessLine = NewAccount(storeId);
        afterBusinessLine.BusinessLineId = "BL00000000000000000000001";
        afterBusinessLine.OnboardingStep = AdyenOnboardingStep.BusinessLineCreated;
        await repo.RecordOnboardingProgress(afterBusinessLine, CancellationToken.None);

        var stored = await repo.GetAdyenAccount(storeId, CancellationToken.None);

        stored.Should().NotBeNull();
        // The second rung must not have erased the first.
        stored!.LegalEntityId.Should().Be("LE00000000000000000000001");
        stored.BusinessLineId.Should().Be("BL00000000000000000000001");
        stored.OnboardingStep.Should().Be(AdyenOnboardingStep.BusinessLineCreated);
        stored.OwnerId.Should().Be(OwnerId);
    }

    [Fact]
    public async Task RecordOnboardingProgress_RefusesToOverwriteAnIdentifierAlreadyRecorded()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetAdyenRepository(client, options);
        var storeId = NewStoreId();

        var first = NewAccount(storeId);
        first.LegalEntityId = "LE00000000000000000000001";
        first.OnboardingStep = AdyenOnboardingStep.LegalEntityCreated;
        await repo.RecordOnboardingProgress(first, CancellationToken.None);

        // A second onboarding of the same store called Adyen too and got a different legal entity.
        var second = NewAccount(storeId);
        second.LegalEntityId = "LE00000000000000000000002";
        second.OnboardingStep = AdyenOnboardingStep.LegalEntityCreated;

        var write = async () => await repo.RecordOnboardingProgress(second, CancellationToken.None);

        var thrown = await write.Should().ThrowAsync<AdyenAccountConflictException>();
        thrown.Which.StoreId.Should().Be(storeId);
        thrown.Which.AttributeName.Should().Be(nameof(AdyenAccount.LegalEntityId));

        // The first identifier is the one in use, and the loser's value is nowhere.
        var stored = await repo.GetAdyenAccount(storeId, CancellationToken.None);
        stored!.LegalEntityId.Should().Be("LE00000000000000000000001");
    }

    [Fact]
    public async Task RecordOnboardingProgress_IncrementsTheVersionOnEveryWrite()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetAdyenRepository(client, options);
        var storeId = NewStoreId();

        var first = NewAccount(storeId);
        first.LegalEntityId = "LE00000000000000000000001";
        var afterFirst = await repo.RecordOnboardingProgress(first, CancellationToken.None);

        var second = NewAccount(storeId);
        second.BusinessLineId = "BL00000000000000000000001";
        var afterSecond = await repo.RecordOnboardingProgress(second, CancellationToken.None);

        afterFirst.AggregateVersion.Should().Be(1);
        afterSecond.AggregateVersion.Should().Be(2);
    }

    [Fact]
    public async Task RecordCapabilities_ReturnsWhatWasThereBefore_SoATransitionCanBePublished()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetAdyenRepository(client, options);
        var storeId = NewStoreId();

        var pending = NewAccount(storeId);
        pending.AccountStatus = PaymentAccountStatus.Pending;
        pending.Capabilities[AdyenCapability.ReceivePayments] = new AdyenCapabilityState
        {
            Requested = true,
            Enabled = true,
            Allowed = false,
            VerificationStatus = "pending",
            LastEventSequence = 1,
        };
        var firstWrite = await repo.RecordCapabilities(pending, CancellationToken.None);

        var ready = NewAccount(storeId);
        ready.AccountStatus = PaymentAccountStatus.ReadyToProcess;
        ready.Capabilities[AdyenCapability.ReceivePayments] = Usable(sequence: 2);
        var secondWrite = await repo.RecordCapabilities(ready, CancellationToken.None);

        firstWrite.Previous.Should().BeNull();
        firstWrite.StatusChanged.Should().BeTrue();

        secondWrite.Previous.Should().NotBeNull();
        secondWrite.Previous!.AccountStatus.Should().Be(PaymentAccountStatus.Pending);
        secondWrite.Current.AccountStatus.Should().Be(PaymentAccountStatus.ReadyToProcess);
        secondWrite.StatusChanged.Should().BeTrue();
    }

    [Fact]
    public async Task RecordCapabilities_ReportsNoChange_WhenTheSameStateArrivesTwice()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetAdyenRepository(client, options);
        var storeId = NewStoreId();

        var account = NewAccount(storeId);
        account.AccountStatus = PaymentAccountStatus.ReadyToProcess;
        account.Capabilities[AdyenCapability.ReceivePayments] = Usable();

        await repo.RecordCapabilities(account, CancellationToken.None);
        var redelivered = await repo.RecordCapabilities(account, CancellationToken.None);

        // Capability updates arrive at least once, so a repeat is ordinary and must not read as a
        // transition worth announcing.
        redelivered.StatusChanged.Should().BeFalse();
    }

    [Fact]
    public async Task RecordCapabilities_RemovesAGraceDeadlineWhenTheGracePeriodEnds()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetAdyenRepository(client, options);
        var storeId = NewStoreId();

        var inGrace = NewAccount(storeId);
        inGrace.AccountStatus = PaymentAccountStatus.ActionRequired;
        inGrace.Capabilities[AdyenCapability.ReceivePayments] = new AdyenCapabilityState
        {
            Requested = true,
            Enabled = true,
            Allowed = true,
            VerificationStatus = "invalid",
            GraceUntil = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            LastEventSequence = 1,
        };
        await repo.RecordCapabilities(inGrace, CancellationToken.None);

        var stillInGrace = await repo.GetAdyenAccount(storeId, CancellationToken.None);
        stillInGrace!.NextGraceDeadline.Should().NotBeNull();

        var resolved = NewAccount(storeId);
        resolved.AccountStatus = PaymentAccountStatus.ReadyToProcess;
        resolved.Capabilities[AdyenCapability.ReceivePayments] = Usable(sequence: 2);
        await repo.RecordCapabilities(resolved, CancellationToken.None);

        var stored = await repo.GetAdyenAccount(storeId, CancellationToken.None);

        // Removed rather than nulled: a null attribute still exists, and would keep the account in any
        // index built to find the ones actually in grace.
        stored!.Capabilities[AdyenCapability.ReceivePayments].GraceUntil.Should().BeNull();
        stored.NextGraceDeadline.Should().BeNull();
    }

    [Fact]
    public async Task CapabilityWritesAndOnboardingWritesDoNotDisturbEachOther()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var repo = fixture.GetAdyenRepository(client, options);
        var storeId = NewStoreId();

        var onboarding = NewAccount(storeId);
        onboarding.LegalEntityId = "LE00000000000000000000001";
        onboarding.AccountHolderId = "AH00000000000000000000001";
        onboarding.OnboardingStep = AdyenOnboardingStep.AccountHolderCreated;
        await repo.RecordOnboardingProgress(onboarding, CancellationToken.None);

        var capabilities = NewAccount(storeId);
        capabilities.AccountStatus = PaymentAccountStatus.ReadyToProcess;
        foreach (var capability in AdyenCapabilities.All)
        {
            capabilities.Capabilities[capability] = Usable();
        }

        await repo.RecordCapabilities(capabilities, CancellationToken.None);

        var stored = await repo.GetAdyenAccount(storeId, CancellationToken.None);

        stored!.LegalEntityId.Should().Be("LE00000000000000000000001");
        stored.AccountHolderId.Should().Be("AH00000000000000000000001");
        stored.OnboardingStep.Should().Be(AdyenOnboardingStep.AccountHolderCreated);
        stored.AccountStatus.Should().Be(PaymentAccountStatus.ReadyToProcess);
        stored.CanProcessPayments.Should().BeTrue();
    }

    [Fact]
    public async Task AdyenWritesLeaveTheProviderCredentialPayloadAlone()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var configurations = fixture.GetRepository(client, options);
        var adyen = fixture.GetAdyenRepository(client, options);
        var storeId = NewStoreId();

        // The recorder owns this attribute on the same record.
        await configurations.UpsertConfiguration(
            new PaymentProviderConfiguration
            {
                StoreId = storeId,
                ProviderId = PaymentConfigurationSchemaProvider.AdyenSortKey,
                TenantId = 4,
                ProviderConfiguration = "{\"credential\":\"kept\"}",
            },
            CancellationToken.None);

        var onboarding = NewAccount(storeId);
        onboarding.LegalEntityId = "LE00000000000000000000001";
        onboarding.OnboardingStep = AdyenOnboardingStep.LegalEntityCreated;
        await adyen.RecordOnboardingProgress(onboarding, CancellationToken.None);

        var configuration = await configurations.GetConfiguration(
            storeId,
            PaymentConfigurationSchemaProvider.AdyenSortKey,
            CancellationToken.None);

        configuration!.ProviderConfiguration.Should().Be("{\"credential\":\"kept\"}");
    }

    [Fact]
    public async Task TheAdyenRecordIsAnOrdinaryProviderConfigurationToAnythingListingTheStore()
    {
        var client = await fixture.GetTestClientAsync();
        var options = await fixture.EstablishExistingTable(client);
        var configurations = fixture.GetRepository(client, options);
        var adyen = fixture.GetAdyenRepository(client, options);
        var storeId = NewStoreId();

        var onboarding = NewAccount(storeId);
        onboarding.LegalEntityId = "LE00000000000000000000001";
        onboarding.OnboardingStep = AdyenOnboardingStep.LegalEntityCreated;
        await adyen.RecordOnboardingProgress(onboarding, CancellationToken.None);

        var forStore = await configurations.GetConfigurationsForStore(storeId, CancellationToken.None);

        // Deliberately not hidden behind a reserved sort key: a store that has an Adyen record should
        // say so when its providers are listed.
        forStore.Should().ContainSingle();
        forStore[0].ProviderId.Should().Be(PaymentConfigurationSchemaProvider.AdyenSortKey);
    }
}
