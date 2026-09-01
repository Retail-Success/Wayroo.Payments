using System.Text.Json;
using AwesomeAssertions;
using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Messages.UnitTests;

/// <summary>
/// Locks in the wire behaviour of the config-sync contract. Orders keeps a read model from this event
/// and routes real payments on it, so the routed detail-type, the scope and the round-trip shape are
/// the contract rather than an implementation detail.
/// </summary>
public class StoreProviderConfigChangedTests
{
    private const string TenantId = "4";
    private const string StoreId = "1007";

    private static StoreProviderConfigChanged Config(
        string acquiringProviderId = ProviderIds.Propay,
        string? previous = null) => new()
    {
        AcquiringProviderId = acquiringProviderId,
        MigrationState = MigrationStates.PropayActive,
        PreviousAcquiringProviderId = previous,
    };

    private static IntegrationEnvelope<StoreProviderConfigChanged> Envelope(
        StoreProviderConfigChanged data,
        long sequence = 1) => IntegrationEnvelope.ForStore(
            data,
            tenantId: TenantId,
            storeId: StoreId,
            correlationId: IntegrationEnvelope.NewCorrelationId(),
            sequence: sequence);

    [Fact]
    public void DetailType_ComposesUnderThePaymentsDomain()
    {
        DetailType.Of<StoreProviderConfigChanged>().Should().Be("payments.StoreProviderConfigChanged.v1");
    }

    [Fact]
    public void Identity_MatchesTheOtherPaymentContracts()
    {
        StoreProviderConfigChanged.Source.Should().Be(PaymentEvents.Source);
        StoreProviderConfigChanged.EventName.Should().Be(nameof(StoreProviderConfigChanged));
        StoreProviderConfigChanged.Version.Should().Be(1);
    }

    [Fact]
    public void IsStoreScoped_AndCarriesBothIdentifiers()
    {
        // ForStore only compiles for IStoreScopedEvent, so this pins the scope level too.
        var json = IntegrationJson.SerializeDetail(Envelope(Config()));

        var scope = JsonDocument.Parse(json).RootElement
            .GetProperty("metadata").GetProperty("scope");
        scope.GetProperty("tenantId").GetString().Should().Be(TenantId);
        scope.GetProperty("storeId").GetString().Should().Be(StoreId);
    }

    [Fact]
    public void RoundTrips_ThroughTheIntegrationEnvelope()
    {
        var envelope = Envelope(Config(ProviderIds.Adyen, previous: ProviderIds.Propay), sequence: 9);

        var roundTripped = JsonSerializer.Deserialize<IntegrationEnvelope<StoreProviderConfigChanged>>(
            IntegrationJson.SerializeDetail(envelope),
            IntegrationJson.Options);

        roundTripped!.Data.AcquiringProviderId.Should().Be(ProviderIds.Adyen);
        roundTripped.Data.PreviousAcquiringProviderId.Should().Be(ProviderIds.Propay);
        roundTripped.Data.MigrationState.Should().Be(MigrationStates.PropayActive);
        roundTripped.Metadata.Sequence.Should().Be(9);
    }

    /// <summary>
    /// The version travels as the envelope's sequence and nowhere else. Carrying it on the payload as
    /// well would let the two disagree, and a consumer would have no way to know which to believe.
    /// </summary>
    [Fact]
    public void TheVersion_TravelsOnlyAsTheEnvelopeSequence()
    {
        var data = JsonDocument.Parse(IntegrationJson.SerializeDetail(Envelope(Config(), sequence: 4)))
            .RootElement.GetProperty("data");

        data.TryGetProperty("configurationVersion", out _).Should().BeFalse();
        data.TryGetProperty("version", out _).Should().BeFalse();
    }

    [Fact]
    public void PreviousProvider_IsOmittedWhenAbsent()
    {
        // Absent rather than null on the wire, matching the other contracts' optional fields.
        var data = JsonDocument.Parse(IntegrationJson.SerializeDetail(Envelope(Config())))
            .RootElement.GetProperty("data");

        data.TryGetProperty("previousAcquiringProviderId", out _).Should().BeFalse();
    }

    /// <summary>
    /// MigrationState is a string, not an enum, and consumers must tolerate a value they have never
    /// seen — a state added mid-migration must not turn every message into a poison message for a
    /// consumer on an older package.
    /// </summary>
    [Fact]
    public void AnUnrecognisedMigrationState_DeserialisesRatherThanThrowing()
    {
        var envelope = Envelope(new StoreProviderConfigChanged
        {
            AcquiringProviderId = ProviderIds.Adyen,
            MigrationState = "SomeStateInventedAfterThisPackageShipped",
        });

        var roundTripped = JsonSerializer.Deserialize<IntegrationEnvelope<StoreProviderConfigChanged>>(
            IntegrationJson.SerializeDetail(envelope),
            IntegrationJson.Options);

        roundTripped!.Data.MigrationState.Should().Be("SomeStateInventedAfterThisPackageShipped");
    }

    [Fact]
    public void MigrationStates_CoverTheDocumentedMachine()
    {
        string[] machine =
        [
            MigrationStates.PropayActive,
            MigrationStates.AdyenOnboardingInvited,
            MigrationStates.AdyenKycInProgress,
            MigrationStates.AdyenReady,
            MigrationStates.AdyenActivePropayTail,
            MigrationStates.AdyenActivePropayClosed,
        ];

        machine.Should().OnlyHaveUniqueItems();
        machine.Should().AllSatisfy(state => state.Should().NotBeNullOrWhiteSpace());
    }
}
