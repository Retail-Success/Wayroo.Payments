using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.DataAccess;

/// <summary>
/// DynamoDB-backed <see cref="IAdyenAccountRepository"/>.
/// </summary>
/// <remarks>
/// Writes the Adyen attributes of a store's <c>adyen</c> provider record and nothing else, so the
/// credential payload and the provider-neutral account attributes beside them survive untouched —
/// the same discipline <see cref="PaymentConfigurationRepository"/> keeps.
/// </remarks>
public class AdyenAccountRepository(
    IAmazonDynamoDB dynamoDbClient,
    IOptions<DynamoDbClientOptions> clientOptions,
    ILogger<AdyenAccountRepository> logger) : IAdyenAccountRepository
{
    private readonly string _tableName = clientOptions.Value.PaymentConfigurationTableName;

    /// <inheritdoc />
    public async Task<AdyenAccount?> GetAdyenAccount(long storeId, CancellationToken cancellationToken)
    {
        var request = new GetItemRequest
        {
            TableName = _tableName,
            Key = PaymentConfigurationSchemaProvider.GetAdyenAccountIdentifiers(storeId),
            // The onboarding ladder decides whether to call Adyen from what this returns. An
            // eventually-consistent read that missed an identifier written moments ago would create a
            // second one — and a duplicate legal entity cannot be deleted.
            ConsistentRead = true,
        };

        var response = await dynamoDbClient.GetItemAsync(request, cancellationToken);

        return response.Item is { Count: > 0 }
            ? PaymentConfigurationSchemaProvider.GetAdyenAccountModel(response.Item)
            : null;
    }

    /// <inheritdoc />
    public async Task<AdyenAccount> RecordOnboardingProgress(
        AdyenAccount account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        var now = DateTimeOffset.UtcNow;
        var update = PaymentConfigurationSchemaProvider.GetAdyenOnboardingUpdate(account, now);
        var guardedAttributes = GuardedIdentifiers(account).ToList();

        var request = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = PaymentConfigurationSchemaProvider.GetAdyenAccountIdentifiers(account.StoreId),
            UpdateExpression = update.UpdateExpression,
            ExpressionAttributeNames = update.ExpressionAttributeNames,
            ExpressionAttributeValues = update.ExpressionAttributeValues,
            ReturnValues = ReturnValue.ALL_NEW,
        };

        if (guardedAttributes.Count > 0)
        {
            // Each identifier may be written once. A concurrent onboarding that got there first keeps
            // its value, and this caller is told rather than left believing it owns an identifier the
            // record does not hold.
            request.ConditionExpression = string.Join(
                " AND ",
                guardedAttributes.Select(attributeName => $"attribute_not_exists(#{attributeName})"));
        }

        logger.LogInformation(
            "Recording Adyen onboarding step {OnboardingStep} for store {StoreId} in {TableName}",
            account.OnboardingStep,
            account.StoreId,
            _tableName);

        try
        {
            var response = await dynamoDbClient.UpdateItemAsync(request, cancellationToken);

            return response.Attributes is { Count: > 0 }
                ? PaymentConfigurationSchemaProvider.GetAdyenAccountModel(response.Attributes)
                : account;
        }
        catch (ConditionalCheckFailedException)
        {
            // Which identifier collided is not in the response, so it is read back rather than
            // guessed at — the caller's next move depends on knowing.
            var stored = await GetAdyenAccount(account.StoreId, cancellationToken);
            var conflicting = guardedAttributes.FirstOrDefault(
                attributeName => IsRecorded(stored, attributeName));

            throw new AdyenAccountConflictException(
                account.StoreId,
                conflicting ?? string.Join(", ", guardedAttributes));
        }
    }

    /// <inheritdoc />
    public async Task<AdyenAccountWriteResult> RecordCapabilities(
        AdyenAccount account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        var now = DateTimeOffset.UtcNow;
        var update = PaymentConfigurationSchemaProvider.GetAdyenCapabilitiesUpdate(account, now);

        var request = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = PaymentConfigurationSchemaProvider.GetAdyenAccountIdentifiers(account.StoreId),
            UpdateExpression = update.UpdateExpression,
            ExpressionAttributeNames = update.ExpressionAttributeNames,
            ExpressionAttributeValues = update.ExpressionAttributeValues,
            // ALL_OLD rather than ALL_NEW: the previous standing is what says whether anything
            // actually changed, and the new state is already in hand. Asking for the new values
            // instead would leave no way to tell a real transition from a repeat without a second,
            // racy read.
            ReturnValues = ReturnValue.ALL_OLD,
        };

        logger.LogInformation(
            "Recording {CapabilityCount} Adyen capabilities for store {StoreId} (status {AccountStatus}) in {TableName}",
            account.Capabilities.Count,
            account.StoreId,
            account.AccountStatus,
            _tableName);

        var response = await dynamoDbClient.UpdateItemAsync(request, cancellationToken);

        var previous = response.Attributes is { Count: > 0 }
            ? PaymentConfigurationSchemaProvider.GetAdyenAccountModel(response.Attributes)
            : null;

        var current = new AdyenAccount
        {
            StoreId = account.StoreId,
            TenantId = account.TenantId ?? previous?.TenantId,
            OwnerId = account.OwnerId ?? previous?.OwnerId,
            LegalEntityId = previous?.LegalEntityId,
            BusinessLineId = previous?.BusinessLineId,
            AccountHolderId = previous?.AccountHolderId,
            BalanceAccountId = previous?.BalanceAccountId,
            OnboardingStep = previous?.OnboardingStep ?? AdyenOnboardingStep.NotStarted,
            OnboardingGeneration = previous?.OnboardingGeneration ?? 1,
            AccountStatus = account.AccountStatus,
            // The stored value is whatever it was plus the increment this write just applied.
            AggregateVersion = (previous?.AggregateVersion ?? 0) + 1,
            CreatedOn = previous?.CreatedOn ?? now,
            ModifiedOn = now,
        };

        foreach (var (capability, state) in account.Capabilities)
        {
            current.Capabilities[capability] = state;
        }

        return new AdyenAccountWriteResult(current, previous);
    }

    // Only identifiers already carried by the caller are guarded: an absent one is a rung that has
    // not run, and guarding it would fail every write that does not happen to set it.
    private static IEnumerable<string> GuardedIdentifiers(AdyenAccount account)
    {
        if (!string.IsNullOrWhiteSpace(account.LegalEntityId))
            yield return nameof(AdyenAccount.LegalEntityId);

        if (!string.IsNullOrWhiteSpace(account.BusinessLineId))
            yield return nameof(AdyenAccount.BusinessLineId);

        if (!string.IsNullOrWhiteSpace(account.AccountHolderId))
            yield return nameof(AdyenAccount.AccountHolderId);

        if (!string.IsNullOrWhiteSpace(account.BalanceAccountId))
            yield return nameof(AdyenAccount.BalanceAccountId);
    }

    private static bool IsRecorded(AdyenAccount? stored, string attributeName) => stored is not null && attributeName switch
    {
        nameof(AdyenAccount.LegalEntityId) => !string.IsNullOrWhiteSpace(stored.LegalEntityId),
        nameof(AdyenAccount.BusinessLineId) => !string.IsNullOrWhiteSpace(stored.BusinessLineId),
        nameof(AdyenAccount.AccountHolderId) => !string.IsNullOrWhiteSpace(stored.AccountHolderId),
        nameof(AdyenAccount.BalanceAccountId) => !string.IsNullOrWhiteSpace(stored.BalanceAccountId),
        _ => false,
    };
}
