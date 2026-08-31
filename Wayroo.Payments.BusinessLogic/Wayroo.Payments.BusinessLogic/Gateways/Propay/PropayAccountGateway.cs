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
        var statusIsProvisional = false;

        if (status is null)
        {
            // The balance is already in hand, and it is what the caller came for. Backfilling the
            // standing is a second, independent provider round trip on top of it, so a failure there
            // is logged and reported rather than thrown: a merchant should still see their money when
            // the provider's account-detail API is having a bad afternoon.
            var refreshed = await TryBackfillAccountStatus(
                tenantId,
                storeId,
                accountNumber.Value,
                cancellationToken);

            // Fail closed on the capability, for the same reason an unrecognised provider status
            // does: not knowing is not permission. StatusIsProvisional is what separates this from a
            // provider that actually said no, so a caller can retry rather than read it as a refusal.
            status = refreshed?.Status ?? PaymentAccountStatus.Pending;
            canProcessPayments = refreshed?.CanProcessPayments ?? false;
            statusIsProvisional = refreshed is null;
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
            StatusIsProvisional = statusIsProvisional,
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
    /// Reads the store's standing from ProPay and records it, tolerating a failure at either step.
    /// </summary>
    /// <remarks>
    /// Only for the balance path, where this is a secondary read whose failure must not cost the
    /// caller the balance that was fetched successfully. <see cref="RefreshAccountDetails"/>
    /// deliberately does not go through here — recording is the whole point of that call, so a
    /// failure there is the caller's to see.
    /// </remarks>
    /// <returns>The standing, or <c>null</c> when it could not be read.</returns>
    private async Task<PaymentAccountDetails?> TryBackfillAccountStatus(
        long tenantId,
        long storeId,
        long accountNumber,
        CancellationToken cancellationToken)
    {
        PaymentAccountDetails details;
        PaymentProviderConfiguration configuration;

        try
        {
            (details, configuration) = await ReadAccountDetails(
                tenantId,
                storeId,
                accountNumber,
                cancellationToken);
        }
        // A caller who hung up is not a failure to absorb — the response is going nowhere. Anything
        // else, a refusal or a transport fault, is: the balance stands on its own.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                exception,
                "Could not read the {ProviderId} account standing for store {StoreId} (tenant {TenantId}) while serving its balance; returning the balance with a provisional status.",
                ProviderId,
                storeId,
                tenantId);

            return null;
        }

        try
        {
            await repository.UpsertAccountDetails(configuration, cancellationToken);
        }
        // The standing was read successfully; only keeping it failed. Serve the real value — the
        // store simply does not heal this time, and the next request reads it from the provider again.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                exception,
                "Read the {ProviderId} account standing for store {StoreId} (tenant {TenantId}) but could not record it; serving it without healing the store.",
                ProviderId,
                storeId,
                tenantId);
        }

        return details;
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
        var (details, configuration) = await ReadAccountDetails(
            tenantId,
            storeId,
            accountNumber,
            cancellationToken);

        await repository.UpsertAccountDetails(configuration, cancellationToken);

        return details;
    }

    /// <summary>
    /// Reads the account detail from ProPay, returning both the neutral contract and the record to
    /// write for it. Split from the write so the balance path can tolerate either half failing.
    /// </summary>
    private async Task<(PaymentAccountDetails Details, PaymentProviderConfiguration Configuration)> ReadAccountDetails(
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

        var details = new PaymentAccountDetails
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

        return (details, configuration);
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
