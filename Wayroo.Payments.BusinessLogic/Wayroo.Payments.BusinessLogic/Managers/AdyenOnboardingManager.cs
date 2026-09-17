using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wayroo.Payments.BusinessLogic.Gateways.Adyen;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.Managers;

/// <summary>
/// Walks a store up the four rungs of Adyen onboarding, recording each identifier as it arrives.
/// </summary>
/// <remarks>
/// <para>
/// The sequence is fixed and each rung needs the one before it:
/// </para>
/// <list type="number">
///   <item><description>the <b>legal entity</b> — who the seller legally is;</description></item>
///   <item><description>the <b>business line</b> — what they sell and through which channels, without
///   which taking a payment can never be permitted;</description></item>
///   <item><description>the <b>account holder</b> — what they are allowed to do, carrying the
///   capabilities;</description></item>
///   <item><description>the <b>balance account</b> — where this store's earnings land.</description></item>
/// </list>
/// <para>
/// <b>What each rung does is read the record, skip if the identifier is already there, call Adyen,
/// and write the answer down immediately.</b> The window between Adyen creating something and this
/// recording it is the only place onboarding can lose track of what exists, so it is kept to a single
/// write and nothing is batched to the end. For a legal entity that window is unrecoverable — Adyen
/// will not delete one and cannot be asked whether one already exists — which is the whole reason for
/// the shape of this class.
/// </para>
/// <para>
/// <b>Concurrency is settled by the database, not by a lock.</b> Two callers onboarding one store
/// both call Adyen, but only the first recording of each identifier is kept; the loser reads what the
/// winner wrote and carries on from there, so both callers end up describing the same accounts.
/// </para>
/// </remarks>
public class AdyenOnboardingManager(
    IAdyenAccountRepository repository,
    IAdyenOnboardingGateway gateway,
    IOptions<AdyenGatewayOptions> options,
    ILogger<AdyenOnboardingManager> logger) : IAdyenOnboardingManager
{
    /// <inheritdoc />
    public async Task<AdyenAccount> Onboard(
        long tenantId,
        long storeId,
        AdyenSellerDetails seller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seller);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storeId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);

        // Resolved before anything is created, not when the business line needs it. A legal entity
        // created for a tenant whose settings are missing could never be finished, and could
        // never be deleted either — so the one cheap check that prevents a permanent orphan happens
        // before the first call to Adyen.
        var tenant = TenantSettings(tenantId);

        var account = await repository.GetAdyenAccount(storeId, cancellationToken)
                      ?? new AdyenAccount { StoreId = storeId };

        GuardTenant(account, tenantId);

        account.TenantId ??= tenantId;
        account.OwnerId ??= seller.OwnerId;

        account = await EnsureLegalEntity(account, seller, cancellationToken);
        account = await EnsureBusinessLine(account, tenant, cancellationToken);
        account = await EnsureAccountHolder(account, cancellationToken);
        account = await EnsureBalanceAccount(account, tenant, cancellationToken);

        logger.LogInformation(
            "Adyen onboarding for store {StoreId} (tenant {TenantId}) stands at {OnboardingStep}.",
            storeId,
            tenantId,
            account.OnboardingStep);

        return account;
    }

    /// <inheritdoc />
    public async Task<Uri?> GetOnboardingLink(
        long storeId,
        string? redirectUrl,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storeId);

        var account = await repository.GetAdyenAccount(storeId, cancellationToken);

        if (string.IsNullOrWhiteSpace(account?.LegalEntityId))
        {
            // Ordinary, not a failure: a store nobody has onboarded yet has nothing to verify.
            logger.LogInformation(
                "No Adyen legal entity is recorded for store {StoreId}, so there is nothing to onboard.",
                storeId);

            return null;
        }

        return await gateway.CreateOnboardingLink(
            new AdyenOnboardingLinkRequest
            {
                StoreId = storeId,
                LegalEntityId = account.LegalEntityId,
                RedirectUrl = redirectUrl,
            },
            cancellationToken);
    }

    /// <summary>
    /// Rung one. Named for what it will become: today it means "this store has no legal entity, so
    /// create one", and the day the platform has a person identifier it will mean "find the one this
    /// seller already has, and reuse it".
    /// </summary>
    /// <remarks>
    /// That change needs no migration and no contract change, which is the point of writing it this
    /// way now — and of recording <see cref="AdyenAccount.OwnerId"/> even while it is always absent.
    /// </remarks>
    private async Task<AdyenAccount> EnsureLegalEntity(
        AdyenAccount account,
        AdyenSellerDetails seller,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(account.LegalEntityId))
            return account;

        var legalEntityId = await gateway.CreateLegalEntity(
            new AdyenLegalEntityRequest
            {
                TenantId = TenantOf(account),
                StoreId = account.StoreId,
                OwnerId = account.OwnerId,
                FirstName = seller.FirstName,
                LastName = seller.LastName,
                ResidentialCountry = seller.ResidentialCountry,
            },
            cancellationToken);

        var recorded = await Record(
            account,
            AdyenOnboardingStep.LegalEntityCreated,
            progress => progress.LegalEntityId = legalEntityId,
            cancellationToken);

        if (!string.Equals(recorded.LegalEntityId, legalEntityId, StringComparison.Ordinal))
        {
            // The one loss that cannot be tidied up. Adyen will not delete a legal entity and cannot
            // be asked to find one by anything we set, so this line is the only record that it
            // exists. Carries PaymentsLogSignals.AdyenLegalEntityOrphaned so the rate can be counted
            // without depending on this sentence's wording.
            logger.LogWarning(
                "{PaymentsSignal}: Adyen legal entity {OrphanedLegalEntityId} was created for store "
                + "{StoreId} but {LegalEntityId} was already recorded, so the new one is unreachable.",
                PaymentsLogSignals.AdyenLegalEntityOrphaned,
                legalEntityId,
                account.StoreId,
                recorded.LegalEntityId);
        }

        return recorded;
    }

    /// <summary>
    /// Rung two. The rung that is easy to mistake for optional, and is not: business-line
    /// verification is what gates taking a payment, so without it a seller can finish every identity
    /// check and still be unable to sell.
    /// </summary>
    private async Task<AdyenAccount> EnsureBusinessLine(
        AdyenAccount account,
        AdyenTenantOptions tenant,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(account.BusinessLineId))
            return account;

        var businessLineId = await gateway.CreateBusinessLine(
            new AdyenBusinessLineRequest
            {
                TenantId = TenantOf(account),
                StoreId = account.StoreId,
                LegalEntityId = Required(account.LegalEntityId, nameof(AdyenAccount.LegalEntityId), account),
                IndustryCode = tenant.IndustryCode,
                SalesChannels = tenant.SalesChannels,
            },
            cancellationToken);

        return await Record(
            account,
            AdyenOnboardingStep.BusinessLineCreated,
            progress => progress.BusinessLineId = businessLineId,
            cancellationToken);
    }

    /// <summary>
    /// Rung three. From here on the balance platform honours the idempotency key, so a retry replays
    /// rather than duplicates — and the recorded state stops being the only thing standing between a
    /// retry and a second account.
    /// </summary>
    private async Task<AdyenAccount> EnsureAccountHolder(
        AdyenAccount account,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(account.AccountHolderId))
            return account;

        var accountHolderId = await gateway.CreateAccountHolder(
            new AdyenAccountHolderRequest
            {
                TenantId = TenantOf(account),
                StoreId = account.StoreId,
                OwnerId = account.OwnerId,
                LegalEntityId = Required(account.LegalEntityId, nameof(AdyenAccount.LegalEntityId), account),
                OnboardingGeneration = account.OnboardingGeneration,
            },
            cancellationToken);

        return await Record(
            account,
            AdyenOnboardingStep.AccountHolderCreated,
            progress => progress.AccountHolderId = accountHolderId,
            cancellationToken);
    }

    /// <summary>
    /// Rung four. A separate call because a new account holder has no balance account of its own —
    /// verified against the live API, where a freshly created holder reports none.
    /// </summary>
    private async Task<AdyenAccount> EnsureBalanceAccount(
        AdyenAccount account,
        AdyenTenantOptions tenant,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(account.BalanceAccountId))
            return account;

        var balanceAccountId = await gateway.CreateBalanceAccount(
            new AdyenBalanceAccountRequest
            {
                TenantId = TenantOf(account),
                StoreId = account.StoreId,
                AccountHolderId = Required(account.AccountHolderId, nameof(AdyenAccount.AccountHolderId), account),
                CurrencyCode = tenant.CurrencyCode,
                OnboardingGeneration = account.OnboardingGeneration,
            },
            cancellationToken);

        return await Record(
            account,
            AdyenOnboardingStep.Complete,
            progress => progress.BalanceAccountId = balanceAccountId,
            cancellationToken);
    }

    /// <summary>
    /// Writes down one rung's identifier and the step now reached, and answers with the record as it
    /// now stands.
    /// </summary>
    /// <remarks>
    /// Carries <b>only the identifier this rung created</b>. Each identifier is written under a
    /// condition that it is not already there, so including one recorded earlier would make every
    /// later rung's write fail against the store's own history.
    /// </remarks>
    private async Task<AdyenAccount> Record(
        AdyenAccount account,
        AdyenOnboardingStep step,
        Action<AdyenAccount> setIdentifier,
        CancellationToken cancellationToken)
    {
        var progress = new AdyenAccount
        {
            StoreId = account.StoreId,
            TenantId = account.TenantId,
            OwnerId = account.OwnerId,
            OnboardingStep = step,
            OnboardingGeneration = account.OnboardingGeneration,
        };

        setIdentifier(progress);

        try
        {
            return await repository.RecordOnboardingProgress(progress, cancellationToken);
        }
        catch (AdyenAccountConflictException conflict)
        {
            // Another onboarding of this store got there first. Its identifier is the one in use, so
            // this reads it back and continues from there — whatever this call created is set aside,
            // not recorded over the top.
            logger.LogInformation(
                "Store {StoreId} already had {ConflictingAttribute} recorded, so Adyen onboarding "
                + "continues from what was already there.",
                account.StoreId,
                conflict.AttributeName);

            return await repository.GetAdyenAccount(account.StoreId, cancellationToken)
                   ?? throw new InvalidOperationException(
                       $"Store {account.StoreId} reported an Adyen identifier conflict but has no "
                       + "record to read it from.");
        }
    }

    /// <summary>
    /// The tenant's settings, or a refusal to start.
    /// </summary>
    /// <remarks>
    /// Deliberately not defaulted. Every value here lands either on an object Adyen will not let us
    /// change or on money in flight, and a guessed industry code produces a business line verified
    /// for a trade the seller is not in.
    /// </remarks>
    private AdyenTenantOptions TenantSettings(long tenantId)
    {
        var key = tenantId.ToString(CultureInfo.InvariantCulture);

        if (!options.Value.Tenants.TryGetValue(key, out var tenant)
            || string.IsNullOrWhiteSpace(tenant.IndustryCode))
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId} has no Adyen settings configured under "
                + $"'{AdyenGatewayConfigurationKeys.TenantSection(tenantId)}', so no store of theirs "
                + "can be onboarded. Nothing was created at Adyen.");
        }

        return tenant;
    }

    /// <summary>
    /// Refuses to onboard a store into a tenant other than the one already recorded against it.
    /// </summary>
    /// <remarks>
    /// The tenant decides which merchant account a sale is processed through and which balance
    /// account is liable for it, and it is written into references that cannot be changed. A store
    /// arriving under a different one than its record says means two parts of the platform disagree
    /// about who a seller sells for, and the wrong answer routes their money to the wrong place.
    /// </remarks>
    private static void GuardTenant(AdyenAccount account, long tenantId)
    {
        if (account.TenantId is { } recorded && recorded != tenantId)
        {
            throw new InvalidOperationException(
                $"Store {account.StoreId} is recorded against tenant {recorded} but was presented for "
                + $"onboarding under tenant {tenantId}.");
        }
    }

    private static long TenantOf(AdyenAccount account)
        => account.TenantId
           ?? throw new InvalidOperationException(
               $"Store {account.StoreId} has no tenant, so the tenant its earnings belong to is "
               + "unknown.");

    // A rung whose predecessor is missing is a bug in the sequence, not a provider problem: the
    // ladder is ordered precisely so this cannot happen.
    private static string Required(string? identifier, string name, AdyenAccount account)
        => string.IsNullOrWhiteSpace(identifier)
            ? throw new InvalidOperationException(
                $"Adyen onboarding reached a step needing {name} for store {account.StoreId}, but it "
                + "is not recorded.")
            : identifier;
}
