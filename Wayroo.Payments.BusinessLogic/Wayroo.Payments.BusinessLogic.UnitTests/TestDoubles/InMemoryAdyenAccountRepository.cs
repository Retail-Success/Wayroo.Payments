using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.UnitTests.TestDoubles;

/// <summary>
/// An <see cref="IAdyenAccountRepository"/> that keeps records in memory.
/// </summary>
/// <remarks>
/// A fake rather than a mock because what the onboarding ladder does depends on what the last write
/// left behind — resuming a part-finished onboarding is the behaviour under test, and a mock returning
/// canned answers could not express it. The write semantics mirror the DynamoDB repository, whose own
/// integration tests pin them against a real table: an identifier already recorded cannot be
/// overwritten, and the version is assigned by the store rather than by the caller.
/// </remarks>
internal sealed class InMemoryAdyenAccountRepository : IAdyenAccountRepository
{
    private readonly Dictionary<long, AdyenAccount> _records = [];

    /// <summary>How many times each store's record has been written, in order.</summary>
    /// <remarks>
    /// What proves each rung persists as it completes rather than everything being written at the
    /// end — the distinction the whole design rests on.
    /// </remarks>
    public List<AdyenOnboardingStep> WrittenSteps { get; } = [];

    /// <summary>Seeds a record, standing in for onboarding that ran before.</summary>
    public void Given(AdyenAccount account) => _records[account.StoreId] = Copy(account);

    /// <summary>
    /// Makes the next write of a given identifier lose a race, as a concurrent onboarding would.
    /// </summary>
    public AdyenAccount? ConflictWith { get; set; }

    public Task<AdyenAccount?> GetAdyenAccount(long storeId, CancellationToken cancellationToken)
        => Task.FromResult(_records.TryGetValue(storeId, out var account) ? Copy(account) : null);

    public Task<AdyenAccount> RecordOnboardingProgress(AdyenAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        // A concurrent onboarding landing between this caller's read and its write.
        if (ConflictWith is { } winner)
        {
            ConflictWith = null;
            _records[account.StoreId] = Copy(winner);

            throw new AdyenAccountConflictException(account.StoreId, FirstIdentifierSet(account));
        }

        if (!_records.TryGetValue(account.StoreId, out var stored))
        {
            stored = new AdyenAccount { StoreId = account.StoreId };
        }

        Guard(stored.LegalEntityId, account.LegalEntityId, nameof(AdyenAccount.LegalEntityId), account.StoreId);
        Guard(stored.BusinessLineId, account.BusinessLineId, nameof(AdyenAccount.BusinessLineId), account.StoreId);
        Guard(stored.AccountHolderId, account.AccountHolderId, nameof(AdyenAccount.AccountHolderId), account.StoreId);
        Guard(stored.BalanceAccountId, account.BalanceAccountId, nameof(AdyenAccount.BalanceAccountId), account.StoreId);

        stored.TenantId = account.TenantId ?? stored.TenantId;
        stored.OwnerId = account.OwnerId ?? stored.OwnerId;
        stored.LegalEntityId = account.LegalEntityId ?? stored.LegalEntityId;
        stored.BusinessLineId = account.BusinessLineId ?? stored.BusinessLineId;
        stored.AccountHolderId = account.AccountHolderId ?? stored.AccountHolderId;
        stored.BalanceAccountId = account.BalanceAccountId ?? stored.BalanceAccountId;
        stored.OnboardingStep = account.OnboardingStep;
        stored.OnboardingGeneration = account.OnboardingGeneration;
        stored.AggregateVersion++;

        _records[account.StoreId] = stored;
        WrittenSteps.Add(account.OnboardingStep);

        return Task.FromResult(Copy(stored));
    }

    public Task<AdyenAccountWriteResult> RecordCapabilities(AdyenAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        _records.TryGetValue(account.StoreId, out var stored);
        var previous = stored is null ? null : Copy(stored);

        var current = stored ?? new AdyenAccount { StoreId = account.StoreId };
        current.AccountStatus = account.AccountStatus;
        current.AggregateVersion++;

        foreach (var (capability, state) in account.Capabilities)
            current.Capabilities[capability] = state;

        _records[account.StoreId] = current;

        return Task.FromResult(new AdyenAccountWriteResult(Copy(current), previous));
    }

    private static void Guard(string? stored, string? incoming, string attributeName, long storeId)
    {
        if (!string.IsNullOrWhiteSpace(stored) && !string.IsNullOrWhiteSpace(incoming))
            throw new AdyenAccountConflictException(storeId, attributeName);
    }

    private static string FirstIdentifierSet(AdyenAccount account)
    {
        if (!string.IsNullOrWhiteSpace(account.LegalEntityId))
            return nameof(AdyenAccount.LegalEntityId);

        if (!string.IsNullOrWhiteSpace(account.BusinessLineId))
            return nameof(AdyenAccount.BusinessLineId);

        if (!string.IsNullOrWhiteSpace(account.AccountHolderId))
            return nameof(AdyenAccount.AccountHolderId);

        return nameof(AdyenAccount.BalanceAccountId);
    }

    private static AdyenAccount Copy(AdyenAccount account)
    {
        var copy = new AdyenAccount
        {
            StoreId = account.StoreId,
            TenantId = account.TenantId,
            OwnerId = account.OwnerId,
            LegalEntityId = account.LegalEntityId,
            BusinessLineId = account.BusinessLineId,
            AccountHolderId = account.AccountHolderId,
            BalanceAccountId = account.BalanceAccountId,
            OnboardingStep = account.OnboardingStep,
            OnboardingGeneration = account.OnboardingGeneration,
            AccountStatus = account.AccountStatus,
            AggregateVersion = account.AggregateVersion,
            CreatedOn = account.CreatedOn,
            ModifiedOn = account.ModifiedOn,
        };

        foreach (var (capability, state) in account.Capabilities)
            copy.Capabilities[capability] = state;

        return copy;
    }
}
