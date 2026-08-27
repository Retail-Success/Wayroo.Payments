using System.Text.Json;
using AwesomeAssertions;
using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Messages.UnitTests;

/// <summary>
/// Locks in the wire behaviour of the provider-neutral payment contracts. These types are consumed
/// from queues by services on older package versions, so the round-trip shape, the routed
/// detail-types and the additive-only versioning guarantees are the contract, not an
/// implementation detail.
/// </summary>
public class PaymentEventTests
{
    private const string TenantId = "jordan-essentials";
    private const string StoreId = "store-42";

    [Fact]
    public void DetailTypes_ComposeUnderThePaymentsDomain()
    {
        DetailType.Of<MerchantAccountStatusChanged>().Should().Be("payments.MerchantAccountStatusChanged.v1");
        DetailType.Of<PaymentSettlementRecorded>().Should().Be("payments.PaymentSettlementRecorded.v1");
        DetailType.Of<PayoutCompleted>().Should().Be("payments.PayoutCompleted.v1");
        DetailType.Of<DisputeOpened>().Should().Be("payments.DisputeOpened.v1");
        DetailType.Of<DisputeStatusChanged>().Should().Be("payments.DisputeStatusChanged.v1");
        DetailType.Of<TransferReturned>().Should().Be("payments.TransferReturned.v1");
    }

    [Fact]
    public void EveryEvent_IsStoreScoped_AndCarriesBothIdentifiers()
    {
        // ForStore only compiles for IStoreScopedEvent, so this pins the scope level of all six.
        string[] payloads =
        [
            Detail(ForStore(MerchantStatus())),
            Detail(ForStore(Settlement())),
            Detail(ForStore(Payout())),
            Detail(ForStore(DisputeOpen())),
            Detail(ForStore(DisputeChange())),
            Detail(ForStore(Returned())),
        ];

        payloads.Should().AllSatisfy(json =>
        {
            JsonElement scope = JsonDocument.Parse(json).RootElement
                .GetProperty("metadata").GetProperty("scope");

            scope.GetProperty("tenantId").GetString().Should().Be(TenantId);
            scope.GetProperty("storeId").GetString().Should().Be(StoreId);
        });
    }

    [Fact]
    public void MerchantAccountStatusChanged_RoundTripsEveryField()
    {
        MerchantAccountStatusChanged roundTripped = RoundTrip(MerchantStatus());

        roundTripped.Should().BeEquivalentTo(MerchantStatus());
        roundTripped.Capabilities.Should().ContainKey("sendToTransferInstrument");
    }

    [Fact]
    public void PaymentSettlementRecorded_RoundTripsEveryField()
    {
        PaymentSettlementRecorded roundTripped = RoundTrip(Settlement());

        roundTripped.Should().BeEquivalentTo(Settlement());
        roundTripped.Fees.Should().HaveCount(2);
    }

    [Fact]
    public void PayoutCompleted_RoundTripsEveryField() =>
        RoundTrip(Payout()).Should().BeEquivalentTo(Payout());

    [Fact]
    public void DisputeOpened_RoundTripsEveryField() =>
        RoundTrip(DisputeOpen()).Should().BeEquivalentTo(DisputeOpen());

    [Fact]
    public void DisputeStatusChanged_RoundTripsEveryField() =>
        RoundTrip(DisputeChange()).Should().BeEquivalentTo(DisputeChange());

    [Fact]
    public void TransferReturned_RoundTripsEveryField() =>
        RoundTrip(Returned()).Should().BeEquivalentTo(Returned());

    [Fact]
    public void Enums_TravelAsStrings()
    {
        Detail(ForStore(MerchantStatus()))
            .Should().Contain("\"status\":\"ActionRequired\"")
            .And.Contain("\"previousStatus\":\"Pending\"");

        Detail(ForStore(DisputeOpen())).Should().Contain("\"disputeType\":\"Chargeback\"");

        Detail(ForStore(Returned())).Should().Contain("\"direction\":\"Outbound\"")
            .And.Contain("\"origin\":\"Webhook\"");
    }

    [Fact]
    public void OptionalFields_AreOmittedWhenNull()
    {
        // The minimum a producer must supply; everything else is additive and stays off the wire.
        var minimal = new MerchantAccountStatusChanged
        {
            ProviderId = ProviderIds.Adyen,
            ProviderAccountRef = "BA_1234",
            Status = MerchantAccountStatus.ReadyToProcess,
            CanProcessPayments = true,
            CanReceivePayouts = true,
        };

        string json = Detail(ForStore(minimal));

        json.Should().NotContain("previousStatus").And.NotContain("actionRequiredBy")
            .And.NotContain("reasonCode").And.NotContain("capabilities");
    }

    [Fact]
    public void UnknownProperty_IsIgnored_SoAnAdditiveMinorDoesNotBreakOlderConsumers()
    {
        // The mechanism behind the additive-minor policy: a producer on a newer package version
        // adds an optional field, and a consumer that has never heard of it keeps working.
        string json = Detail(ForStore(Payout())).Replace(
            "\"providerId\"",
            "\"payoutSpeedAddedInALaterMinor\":\"instant\",\"providerId\"",
            StringComparison.Ordinal);

        var roundTripped = JsonSerializer.Deserialize<IntegrationEnvelope<PayoutCompleted>>(
            json, IntegrationJson.Options);

        roundTripped!.Data.ProviderPayoutRef.Should().Be("PO_5551");
    }

    [Fact]
    public void UnknownEnumMember_Throws_WhichIsWhyANewMemberIsABreakingChange()
    {
        // Pins the reasoning documented on PaymentEvents: enum members cannot be added additively,
        // because an older consumer turns every message carrying the new value into a poison
        // message. If this ever stops throwing, the versioning policy needs rewriting.
        string json = Detail(ForStore(MerchantStatus())).Replace(
            "\"status\":\"ActionRequired\"",
            "\"status\":\"UnderReviewAddedLater\"",
            StringComparison.Ordinal);

        Action act = () => JsonSerializer.Deserialize<IntegrationEnvelope<MerchantAccountStatusChanged>>(
            json, IntegrationJson.Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Money_TravelsAsANumberAndKeepsItsScale()
    {
        // A decimal, not a double, and not a string: settlement reconciles to the penny.
        Detail(ForStore(Settlement())).Should()
            .Contain("\"grossAmount\":{\"amount\":124.99,\"currency\":\"USD\"}");

        RoundTrip(Settlement()).GrossAmount.Amount.Should().Be(124.99m);
    }

    [Fact]
    public void Money_QuotedAmount_IsRejectedAsAPoisonMessage()
    {
        string json = Detail(ForStore(Payout())).Replace(
            "\"amount\":250.00", "\"amount\":\"250.00\"", StringComparison.Ordinal);

        Action act = () => JsonSerializer.Deserialize<IntegrationEnvelope<PayoutCompleted>>(
            json, IntegrationJson.Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Money_Factories_NormalizeTheCurrencyCode()
    {
        Money.Of(12.34m, " usd ").Should().Be(Money.Usd(12.34m));
        Money.Usd(12.34m).ToString().Should().Be("12.34 USD");
    }

    [Fact]
    public void Money_BlankCurrency_Throws()
    {
        Action act = () => Money.Of(12.34m, "  ");

        act.Should().Throw<ArgumentException>();
    }

    private static IntegrationEnvelope<TData> ForStore<TData>(TData data)
        where TData : IStoreScopedEvent =>
        IntegrationEnvelope.ForStore(data, TenantId, StoreId, IntegrationEnvelope.NewCorrelationId());

    private static string Detail<TData>(IntegrationEnvelope<TData> envelope) =>
        IntegrationJson.SerializeDetail(envelope);

    private static TData RoundTrip<TData>(TData data) where TData : IStoreScopedEvent =>
        JsonSerializer.Deserialize<IntegrationEnvelope<TData>>(
            Detail(ForStore(data)), IntegrationJson.Options)!.Data;

    private static MerchantAccountStatusChanged MerchantStatus() => new()
    {
        ProviderId = ProviderIds.Adyen,
        ProviderAccountRef = "BA_3227C06Z223222G5J8VN7QZ",
        Status = MerchantAccountStatus.ActionRequired,
        PreviousStatus = MerchantAccountStatus.Pending,
        CanProcessPayments = true,
        CanReceivePayouts = false,
        ActionRequiredBy = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
        Reason = "Proof of address needed for the beneficial owner.",
        ReasonCode = "dataMissing",
        Capabilities = new Dictionary<string, string>
        {
            ["receivePayments"] = "valid",
            ["sendToTransferInstrument"] = "pending",
        },
    };

    private static PaymentSettlementRecorded Settlement() => new()
    {
        ProviderId = ProviderIds.Adyen,
        ProviderAccountRef = "BA_3227C06Z223222G5J8VN7QZ",
        ProviderPaymentRef = "NC6HT9CRT65ZGN82",
        ProviderCaptureRef = "JS8HT9CRT65ZGN99",
        OrderReference = "ORD-1042",
        SettledAt = new DateTimeOffset(2026, 8, 20, 3, 0, 0, TimeSpan.Zero),
        GrossAmount = Money.Usd(124.99m),
        FeeAmount = Money.Usd(3.87m),
        NetAmount = Money.Usd(115.12m),
        CommissionAmount = Money.Usd(6.00m),
        Fees =
        [
            new SettlementFee { FeeType = SettlementFeeTypes.Interchange, Amount = Money.Usd(2.15m) },
            new SettlementFee { FeeType = SettlementFeeTypes.Scheme, Amount = Money.Usd(1.72m) },
        ],
        ProviderBatchRef = "accounting-2026-08-20",
        Origin = RecordOrigin.Report,
    };

    private static PayoutCompleted Payout() => new()
    {
        ProviderId = ProviderIds.Adyen,
        ProviderAccountRef = "BA_3227C06Z223222G5J8VN7QZ",
        ProviderPayoutRef = "PO_5551",
        Amount = Money.Usd(250.00m),
        PaidOutAt = new DateTimeOffset(2026, 8, 21, 6, 0, 0, TimeSpan.Zero),
        ExpectedArrivalAt = new DateTimeOffset(2026, 8, 23, 6, 0, 0, TimeSpan.Zero),
        DestinationRef = "SE_TRANSFER_INSTRUMENT_1",
        DestinationLast4 = "4821",
        ProviderBatchRef = "payout-2026-08-21",
        Origin = RecordOrigin.Report,
    };

    private static DisputeOpened DisputeOpen() => new()
    {
        ProviderId = ProviderIds.Adyen,
        ProviderAccountRef = "BA_3227C06Z223222G5J8VN7QZ",
        ProviderDisputeRef = "DSP_9911",
        ProviderPaymentRef = "NC6HT9CRT65ZGN82",
        DisputeType = DisputeType.Chargeback,
        DisputedAmount = Money.Usd(124.99m),
        OpenedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        DefenseDueBy = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero),
        FundsDebited = true,
        ReasonCode = "10.4",
        Reason = "Other fraud - card absent environment.",
        OrderReference = "ORD-1042",
    };

    private static DisputeStatusChanged DisputeChange() => new()
    {
        ProviderId = ProviderIds.Adyen,
        ProviderAccountRef = "BA_3227C06Z223222G5J8VN7QZ",
        ProviderDisputeRef = "DSP_9911",
        ProviderPaymentRef = "NC6HT9CRT65ZGN82",
        Status = DisputeStatus.Won,
        PreviousStatus = DisputeStatus.Defended,
        Amount = Money.Usd(124.99m),
        ResolvedAt = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero),
        DefenseDueBy = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero),
        ReasonCode = "represented",
        Reason = "Defense accepted by the issuer.",
    };

    private static TransferReturned Returned() => new()
    {
        ProviderId = ProviderIds.Adyen,
        ProviderAccountRef = "BA_3227C06Z223222G5J8VN7QZ",
        ProviderTransferRef = "PO_5551",
        Direction = TransferDirection.Outbound,
        Amount = Money.Usd(250.00m),
        ReturnedAt = new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero),
        ReasonCode = "R03",
        Reason = "No account or unable to locate account.",
        CounterpartyLast4 = "4821",
        Origin = RecordOrigin.Webhook,
    };
}
