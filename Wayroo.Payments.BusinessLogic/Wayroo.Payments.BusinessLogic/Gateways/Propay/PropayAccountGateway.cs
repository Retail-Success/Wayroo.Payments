using System.Text.Json;
using Microsoft.Extensions.Logging;
using RetailSuccess.PaymentGateway.Propay;
using RetailSuccess.PaymentGateway.Propay.Abstractions;
using RetailSuccess.PaymentGateway.Propay.Requests;
using Wayroo.Payments.Models;
using PropayAccountDetail = RetailSuccess.PaymentGateway.Propay.Responses.GetAccountDetailsResponse;

namespace Wayroo.Payments.BusinessLogic.Gateways.Propay;

/// <summary>
/// ProPay implementation of <see cref="IPaymentAccountGateway"/>. Replaces the balance and account
/// reads that Luci.Orders' <c>StorePropayController</c> serves today.
/// </summary>
/// <remarks>
/// <para>
/// <b>Balances arrive in cents.</b> ProPay's REST balance endpoint returns <see cref="int"/> minor
/// units; Luci.Orders converts them with <c>Money&lt;USD&gt;</c>. Every amount crossing this seam is
/// divided by <see cref="MinorUnitsPerMajorUnit"/> — a missed conversion is a hundredfold error, and
/// a silent one.
/// </para>
/// <para>
/// <b>The two provider reads are not interchangeable.</b> Balance comes from the REST
/// <c>MerchantBalanceDetails</c> call, which is also the only live source of payout capability
/// (<c>achOut.enabled</c>). Standing comes from the XML account-detail call, which does not report
/// payout capability at all. Neither can answer the other's question.
/// </para>
/// </remarks>
public class PropayAccountGateway(
    IPropayClient propayClient,
    IPaymentConfigurationRepository repository,
    ILogger<PropayAccountGateway> logger) : IPaymentAccountGateway
{
    /// <summary>ProPay reports balances in cents; the contract exposes major units.</summary>
    private const decimal MinorUnitsPerMajorUnit = 100m;

    /// <summary>ProPay accounts settle in US dollars; the account detail confirms it per account.</summary>
    private const string DefaultCurrency = "USD";

    /// <summary>ProPay spells its booleans as single characters.</summary>
    private const string PropayYes = "Y";

    /// <inheritdoc />
    public string ProviderId => "propay";

    /// <inheritdoc />
    public async Task<PaymentAccountBalance?> GetBalance(
        long tenantId,
        long storeId,
        CancellationToken cancellationToken)
    {
        var accountNumber = await ResolveAccountNumber(storeId, providerAccountRef: null, cancellationToken);
        if (accountNumber is null)
        {
            logger.LogInformation(
                "No {ProviderId} account reference recorded for store {StoreId} (tenant {TenantId}); reporting the account as absent.",
                ProviderId,
                storeId,
                tenantId);
            return null;
        }

        var balance = Unwrap(
            await propayClient.GetAccountBalance(tenantId, accountNumber.Value),
            "read the account balance");

        // Standing is not on the balance response. Prefer what we already recorded; when nothing has
        // been recorded for this store yet, read it now and keep it. The contract promises a non-null
        // status whenever the account exists and downstream consumers cast it unconditionally, so
        // "this store has not been backfilled yet" must not surface as a null.
        var recorded = await repository.GetConfiguration(storeId, ProviderId, cancellationToken);
        var status = recorded?.AccountStatus;
        var canProcessPayments = status == PaymentAccountStatus.ReadyToProcess;

        if (status is null)
        {
            var refreshed = await ReadAndRecordAccountDetails(
                tenantId,
                storeId,
                accountNumber.Value,
                cancellationToken);

            status = refreshed.Status;
            canProcessPayments = refreshed.CanProcessPayments;
        }

        return new PaymentAccountBalance
        {
            AccountExists = true,
            ProviderId = ProviderId,
            ProviderAccountRef = accountNumber.Value.ToString(),
            AvailableBalance = ToMajorUnits(balance.AvailableBalance),
            PendingBalance = ToMajorUnits(balance.PendingBalance),
            ReserveBalance = ToMajorUnits(balance.ReserveBalance),
            Status = status,
            CanProcessPayments = canProcessPayments,
            // achOut is the merchant's own direct-deposit account. When ProPay disables it the
            // account keeps taking money and the balance simply accumulates until it is restored,
            // which is exactly why this is separate from CanProcessPayments.
            CanReceivePayouts = IsEnabled(balance.AchOut?.Enabled),
        };
    }

    /// <inheritdoc />
    public async Task<PaymentAccountDetails?> RefreshAccountDetails(
        long tenantId,
        long storeId,
        string? providerAccountRef,
        CancellationToken cancellationToken)
    {
        var accountNumber = await ResolveAccountNumber(storeId, providerAccountRef, cancellationToken);
        if (accountNumber is null)
        {
            logger.LogInformation(
                "No {ProviderId} account reference recorded for store {StoreId} (tenant {TenantId}) and none supplied; nothing to refresh.",
                ProviderId,
                storeId,
                tenantId);
            return null;
        }

        return await ReadAndRecordAccountDetails(tenantId, storeId, accountNumber.Value, cancellationToken);
    }

    /// <summary>
    /// Reads the account detail from ProPay and records it against the store's payment configuration.
    /// </summary>
    private async Task<PaymentAccountDetails> ReadAndRecordAccountDetails(
        long tenantId,
        long storeId,
        long accountNumber,
        CancellationToken cancellationToken)
    {
        var detail = Unwrap(
            await propayClient.GetAccountDetails(
                tenantId,
                new GetAccountDetailsRequest(accountNumber.ToString())),
            "read the account details");

        var status = PropayAccountStatusMap.Map(detail.AccountStatus);
        if (status is null)
        {
            // Fail closed. An unrecognised status is far more likely to be a new restrictive state
            // than a new healthy one, and reading it as sellable is the expensive mistake.
            logger.LogWarning(
                "Unrecognised {ProviderId} account status {ProviderStatus} for store {StoreId}; treating the account as suspended.",
                ProviderId,
                detail.AccountStatus,
                storeId);
            status = PaymentAccountStatus.Suspended;
        }

        var currency = string.IsNullOrWhiteSpace(detail.CurrencyCode) ? DefaultCurrency : detail.CurrencyCode;
        var refreshedOn = DateTimeOffset.UtcNow;

        var configuration = new PaymentProviderConfiguration
        {
            StoreId = storeId,
            TenantId = tenantId,
            ProviderId = ProviderId,
            AccountId = accountNumber.ToString(),
            ProviderAccountDetails = SerializeProviderDetail(detail),
            AccountStatus = status,
            AccountDetailsRefreshedOn = refreshedOn,
        };

        await repository.UpsertAccountDetails(configuration, cancellationToken);

        return new PaymentAccountDetails
        {
            AccountExists = true,
            ProviderId = ProviderId,
            ProviderAccountRef = accountNumber.ToString(),
            Status = status,
            ProviderStatusCode = detail.AccountStatus,
            CanProcessPayments = status == PaymentAccountStatus.ReadyToProcess,
            ApiReady = IsEnabled(detail.ApiReady),
            Tier = detail.Tier,
            Affiliation = detail.Affiliation,
            CurrencyCode = currency,
            SignupDate = ToOffset(detail.SignupDate),
            Expiration = ToOffset(detail.Expiration),
            // Unlike the balance endpoint's cents, the account detail reports major units already.
            ReserveBalance = MoneyAmount.Of(detail.ReserveBalance, currency),
            CardTransactionLimit = MoneyAmount.Of(detail.CreditCardTransactionLimit, currency),
            CardMonthlyLimit = MoneyAmount.Of(detail.CreditCardMonthLimit, currency),
            AchTransactionLimit = MoneyAmount.Of(detail.AchPaymentPerTranLimit, currency),
            AchMonthlyLimit = MoneyAmount.Of(detail.AchPaymentMonthLimit, currency),
            RefreshedOn = refreshedOn,
        };
    }

    /// <summary>
    /// Finds the ProPay account number to act against: the one the caller supplied, otherwise the one
    /// already recorded for the store.
    /// </summary>
    /// <remarks>
    /// There is deliberately no third source. This service does not call another Wayroo service to go
    /// looking for an account it has not been told about — a store it holds no reference for is
    /// answered as having no account, and a backfill seeds it by supplying
    /// <c>providerAccountRef</c>.
    /// </remarks>
    private async Task<long?> ResolveAccountNumber(
        long storeId,
        string? providerAccountRef,
        CancellationToken cancellationToken)
    {
        if (long.TryParse(providerAccountRef, out var suppliedAccountNumber) && suppliedAccountNumber > 0)
            return suppliedAccountNumber;

        var recorded = await repository.GetConfiguration(storeId, ProviderId, cancellationToken);

        return long.TryParse(recorded?.AccountId, out var recordedAccountNumber) && recordedAccountNumber > 0
            ? recordedAccountNumber
            : null;
    }

    /// <summary>
    /// Unwraps a ProPay result, turning a refusal into a <see cref="PaymentProviderException"/>.
    /// </summary>
    /// <remarks>
    /// A refusal is a well-formed request the provider declined, and it surfaces to the caller as a
    /// 400. Transport failures throw out of the client instead and are left to become a 500 — the
    /// caller can usefully retry those, and can do nothing about the former.
    /// </remarks>
    private static T Unwrap<T>(PropayResult<T> result, string attempted)
        => result is PropayResult<T>.Success success
            ? success.Value
            : throw new PaymentProviderException(
                string.IsNullOrWhiteSpace(result.Status.Message)
                    ? $"The payment provider refused to {attempted}."
                    : result.Status.Message,
                result.Status.Code,
                result.Status.Notes);

    private static MoneyAmount ToMajorUnits(int minorUnits)
        => MoneyAmount.Of(minorUnits / MinorUnitsPerMajorUnit, DefaultCurrency);

    private static bool IsEnabled(string? propayFlag)
        => string.Equals(propayFlag?.Trim(), PropayYes, StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? ToOffset(DateTime? value)
        => value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    /// <summary>
    /// Renders the provider's account detail for storage. Hand-built rather than reflected over the
    /// whole response: it keeps the stored shape stable when the gateway package adds fields, and it
    /// is the one place to notice if something sensitive would start being persisted. Note what is
    /// left out — the source email is account PII and no consumer of this service needs it.
    /// </summary>
    private static string SerializeProviderDetail(PropayAccountDetail detail)
        => JsonSerializer.Serialize(new
        {
            accountNumber = detail.AccountNumber,
            accountStatus = detail.AccountStatus,
            affiliation = detail.Affiliation,
            apiReady = detail.ApiReady,
            currencyCode = detail.CurrencyCode,
            tier = detail.Tier,
            signupDate = detail.SignupDate,
            expiration = detail.Expiration,
            creditCardTransactionLimit = detail.CreditCardTransactionLimit,
            creditCardMonthLimit = detail.CreditCardMonthLimit,
            achPaymentPerTranLimit = detail.AchPaymentPerTranLimit,
            achPaymentMonthLimit = detail.AchPaymentMonthLimit,
            creditCardMonthlyVolume = detail.CreditCardMonthlyVolume,
            achPaymentMonthlyVolume = detail.AchPaymentMonthlyVolume,
            reserveBalance = detail.ReserveBalance,
        });
}
