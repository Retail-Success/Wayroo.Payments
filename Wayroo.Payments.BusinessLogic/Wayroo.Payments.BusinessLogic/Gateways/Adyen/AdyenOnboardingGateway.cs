using System.Globalization;
using Adyen.BalancePlatform.Services;
using Adyen.Core.Client;
using Adyen.LegalEntityManagement.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wayroo.Payments.Models;
using BclModels = Adyen.BalancePlatform.Models;
using LemModels = Adyen.LegalEntityManagement.Models;

namespace Wayroo.Payments.BusinessLogic.Gateways.Adyen;

/// <summary>
/// <see cref="IAdyenOnboardingGateway"/> over Adyen's Legal Entity Management and balance platform
/// APIs.
/// </summary>
/// <remarks>
/// <para>
/// All the Adyen vocabulary lives here — the two planes' request shapes, the reference written on
/// each object, the idempotency header — so that the manager above deals only in store identifiers
/// and the identifiers that come back.
/// </para>
/// <para>
/// <b>The reference on every object is written once, at creation, and read for the life of the
/// account.</b> Adyen neither enforces uniqueness on it nor offers any way to search by it, so it
/// buys nothing except the ability of a human or a report to tie an Adyen object back to a store.
/// That is worth having, and it is only available at creation.
/// </para>
/// </remarks>
public class AdyenOnboardingGateway(
    ILegalEntitiesService legalEntities,
    IBusinessLinesService businessLines,
    IAccountHoldersService accountHolders,
    IBalanceAccountsService balanceAccounts,
    IHostedOnboardingService hostedOnboarding,
    IOptions<AdyenGatewayOptions> options,
    ILogger<AdyenOnboardingGateway> logger) : IAdyenOnboardingGateway
{
    private const int MaxProviderDetailLength = 512;

    /// <inheritdoc />
    public async Task<string> CreateLegalEntity(
        AdyenLegalEntityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reference = SellerReference(
            request.TenantId,
            request.StoreId,
            request.OwnerId,
            AdyenReferenceType.LegalEntity);

        var payload = new LemModels.LegalEntityInfoRequiredType
        {
            Type = LemModels.LegalEntityInfoRequiredType.TypeEnum.Individual,
            Reference = reference.ToString(),
            Individual = new LemModels.Individual
            {
                Name = new LemModels.Name
                {
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                },
                // The country and nothing else, because it is all Adyen needs to create the entity
                // and all this platform should be holding: the seller gives the rest to Adyen.
                ResidentialAddress = new LemModels.Address { Country = request.ResidentialCountry },
            },
        };

        var response = await legalEntities.CreateLegalEntityAsync(payload, cancellationToken: cancellationToken);

        if (!response.TryDeserializeOkResponse(out LemModels.LegalEntity? created)
            || string.IsNullOrWhiteSpace(created?.Id))
        {
            throw LemRefused(response, "create a legal entity", request.StoreId);
        }

        logger.LogInformation(
            "Created Adyen legal entity {LegalEntityId} for store {StoreId} with reference {AdyenReference}.",
            created.Id,
            request.StoreId,
            reference);

        return created.Id;
    }

    /// <inheritdoc />
    public async Task<string> CreateBusinessLine(
        AdyenBusinessLineRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new LemModels.BusinessLineInfo
        {
            LegalEntityId = request.LegalEntityId,
            Service = LemModels.BusinessLineInfo.ServiceEnum.PaymentProcessing,
            IndustryCode = request.IndustryCode,
            SalesChannels = [.. request.SalesChannels],
            // No web data. A seller starting at the point of sale has no website to verify, and an
            // empty list would ask Adyen to verify nothing rather than say there is nothing to
            // verify. Online selling adds it later, along with the exemption reason if there is none.
        };

        var response = await businessLines.CreateBusinessLineAsync(payload, cancellationToken: cancellationToken);

        if (!response.TryDeserializeOkResponse(out LemModels.BusinessLine? created)
            || string.IsNullOrWhiteSpace(created?.Id))
        {
            throw LemRefused(response, "create a business line", request.StoreId);
        }

        logger.LogInformation(
            "Created Adyen business line {BusinessLineId} for store {StoreId} on legal entity {LegalEntityId}.",
            created.Id,
            request.StoreId,
            request.LegalEntityId);

        return created.Id;
    }

    /// <inheritdoc />
    public async Task<string> CreateAccountHolder(
        AdyenAccountHolderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = options.Value;
        var reference = SellerReference(
            request.TenantId,
            request.StoreId,
            request.OwnerId,
            AdyenReferenceType.AccountHolder);
        var idempotencyKey = AdyenIdempotencyKey.ForAccountHolder(
            settings.ReferenceEnvironment,
            request.StoreId,
            request.OnboardingGeneration);

        var payload = new BclModels.AccountHolderInfo
        {
            LegalEntityId = request.LegalEntityId,
            BalancePlatform = settings.BalancePlatformId,
            Reference = reference.ToString(),
            Description = DescriptionFor(request.StoreId),
            // Every capability the platform needs, asked for at creation. They come back requested
            // but not yet permitted, and Adyen reports them becoming usable over webhooks — so
            // nothing is recorded from this response. Requesting them afterwards would be another
            // call, and another thing to be left half-done.
            Capabilities = AdyenCapabilities.All.ToDictionary(
                capability => capability.ToAdyenName(),
                _ => new BclModels.AccountHolderCapability { Requested = true }),
        };

        var response = await accountHolders.CreateAccountHolderAsync(
            payload,
            WithIdempotencyKey(idempotencyKey),
            cancellationToken);

        if (!response.TryDeserializeOkResponse(out BclModels.AccountHolder? created)
            || string.IsNullOrWhiteSpace(created?.Id))
        {
            throw BalancePlatformRefused(response, "create an account holder", request.StoreId);
        }

        logger.LogInformation(
            "Created Adyen account holder {AccountHolderId} for store {StoreId} with reference {AdyenReference} "
            + "under idempotency key {IdempotencyKey}.",
            created.Id,
            request.StoreId,
            reference,
            idempotencyKey);

        return created.Id;
    }

    /// <inheritdoc />
    public async Task<string> CreateBalanceAccount(
        AdyenBalanceAccountRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = options.Value;

        var reference = AdyenReference.ForStore(
            settings.ReferenceEnvironment,
            request.TenantId,
            request.StoreId,
            AdyenReferenceType.BalanceAccount);

        var idempotencyKey = AdyenIdempotencyKey.ForBalanceAccount(
            settings.ReferenceEnvironment,
            request.StoreId,
            request.OnboardingGeneration);

        var payload = new BclModels.BalanceAccountInfo
        {
            AccountHolderId = request.AccountHolderId,
            // Stated, never left to Adyen: the default is EUR even on a US platform, and the
            // currency cannot be changed once the account exists.
            DefaultCurrencyCode = request.CurrencyCode,
            Reference = reference.ToString(),
            Description = DescriptionFor(request.StoreId),
        };

        var response = await balanceAccounts.CreateBalanceAccountAsync(
            payload,
            WithIdempotencyKey(idempotencyKey),
            cancellationToken);

        if (!response.TryDeserializeOkResponse(out BclModels.BalanceAccount? created)
            || string.IsNullOrWhiteSpace(created?.Id))
        {
            throw BalancePlatformRefused(response, "create a balance account", request.StoreId);
        }

        logger.LogInformation(
            "Created Adyen balance account {BalanceAccountId} in {CurrencyCode} for store {StoreId} with "
            + "reference {AdyenReference} under idempotency key {IdempotencyKey}.",
            created.Id,
            request.CurrencyCode,
            request.StoreId,
            reference,
            idempotencyKey);

        return created.Id;
    }

    /// <inheritdoc />
    public async Task<Uri> CreateOnboardingLink(
        AdyenOnboardingLinkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new LemModels.OnboardingLinkInfo
        {
            RedirectUrl = request.RedirectUrl,
            Locale = request.Locale,
            ThemeId = request.ThemeId,
        };

        var response = await hostedOnboarding.GetLinkToAdyenhostedOnboardingPageAsync(
            request.LegalEntityId,
            payload,
            cancellationToken: cancellationToken);

        if (!response.TryDeserializeOkResponse(out LemModels.OnboardingLink? link)
            || !Uri.TryCreate(link?.Url, UriKind.Absolute, out var url))
        {
            // Alone among these operations, this one declares no error body — so there is nothing to
            // read but the status and whatever Adyen happened to write, which is why the raw content
            // is carried through here and nowhere else.
            throw new PaymentProviderException(
                $"Adyen refused to create an onboarding link for store {request.StoreId} "
                + $"({(int)response.StatusCode} {response.ReasonPhrase}).",
                ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
                Truncate(response.RawContent));
        }

        // The link itself is deliberately not logged. It authenticates the seller into their own
        // onboarding session, so anyone holding it can act as them.
        logger.LogInformation(
            "Created an Adyen onboarding link for store {StoreId} on legal entity {LegalEntityId}.",
            request.StoreId,
            request.LegalEntityId);

        return url;
    }

    /// <summary>
    /// The reference for an object belonging to the seller as a person rather than to one of their
    /// stores — their legal entity and their account holder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Person-scoped only when there really is a person to scope it to.</b> While the platform
    /// holds no person identity, these fall back to the store the object was created for. That is a
    /// weaker claim than the object deserves — a legal entity belongs to a human, not to one of their
    /// selling relationships — but it is a true one, and a reference is written once and then read
    /// for the life of the account.
    /// </para>
    /// <para>
    /// The alternative was to put the store identifier in the person segment, which reads as a person
    /// key without being one, and would leave every reference written before the real key arrived
    /// indistinguishable from the ones written after. Nothing is lost by waiting: finding a returning
    /// seller's legal entity is answered from our own record and never from the reference, because
    /// Adyen cannot search on it.
    /// </para>
    /// </remarks>
    private AdyenReference SellerReference(
        long tenantId,
        long storeId,
        Guid? ownerId,
        AdyenReferenceType type)
        => ownerId is { } person
            ? AdyenReference.ForPerson(options.Value.ReferenceEnvironment, person, type)
            : AdyenReference.ForStore(options.Value.ReferenceEnvironment, tenantId, storeId, type);

    private static RequestOptions WithIdempotencyKey(AdyenIdempotencyKey key)
    {
        var requestOptions = new RequestOptions();
        requestOptions.Headers[AdyenIdempotencyKey.HeaderName] = key.ToString();

        return requestOptions;
    }

    // Shown against the account in Adyen's Customer Area, where someone looking into a seller's
    // account otherwise has no way to tell which store they are looking at.
    private static string DescriptionFor(long storeId)
        => string.Create(CultureInfo.InvariantCulture, $"Wayroo store {storeId}");

    /// <summary>
    /// Turns a Legal Entity Management refusal into a <see cref="PaymentProviderException"/>.
    /// </summary>
    /// <remarks>
    /// Generic over the response rather than taking a shared interface, because every operation has
    /// its own response type; the constraints are what prove the one passed in really does carry the
    /// error bodies being read.
    /// </remarks>
    private static PaymentProviderException LemRefused<TResponse>(
        TResponse response,
        string attempted,
        long storeId)
        where TResponse : IApiResponse,
        IBadRequest<LemModels.ServiceError?>,
        IUnauthorized<LemModels.ServiceError?>,
        IForbidden<LemModels.ServiceError?>,
        IUnprocessableContent<LemModels.ServiceError?>,
        IInternalServerError<LemModels.ServiceError?>
    {
        LemModels.ServiceError? error = null;

        _ = response.TryDeserializeBadRequestResponse(out error)
            || response.TryDeserializeUnprocessableContentResponse(out error)
            || response.TryDeserializeForbiddenResponse(out error)
            || response.TryDeserializeUnauthorizedResponse(out error)
            || response.TryDeserializeInternalServerErrorResponse(out error);

        return new PaymentProviderException(
            string.IsNullOrWhiteSpace(error?.Message)
                ? Fallback(attempted, storeId, response)
                : error.Message,
            error?.ErrorCode,
            // The PSP reference is the first thing Adyen support asks for, and the only handle on a
            // request once it has left this service.
            error?.PspReference);
    }

    /// <summary>
    /// Turns a balance platform refusal into a <see cref="PaymentProviderException"/>.
    /// </summary>
    /// <remarks>
    /// The two planes report errors in different shapes, which is why this is not shared with
    /// <see cref="LemRefused"/>. Note what is left out: the balance platform also lists the fields it
    /// rejected and echoes the values it was sent, and those values are the seller's own details.
    /// </remarks>
    private static PaymentProviderException BalancePlatformRefused<TResponse>(
        TResponse response,
        string attempted,
        long storeId)
        where TResponse : IApiResponse,
        IBadRequest<BclModels.RestServiceError?>,
        IUnauthorized<BclModels.RestServiceError?>,
        IForbidden<BclModels.RestServiceError?>,
        IUnprocessableContent<BclModels.RestServiceError?>,
        IInternalServerError<BclModels.RestServiceError?>
    {
        BclModels.RestServiceError? error = null;

        _ = response.TryDeserializeBadRequestResponse(out error)
            || response.TryDeserializeUnprocessableContentResponse(out error)
            || response.TryDeserializeForbiddenResponse(out error)
            || response.TryDeserializeUnauthorizedResponse(out error)
            || response.TryDeserializeInternalServerErrorResponse(out error);

        var message = error?.Detail ?? error?.Title;

        return new PaymentProviderException(
            string.IsNullOrWhiteSpace(message) ? Fallback(attempted, storeId, response) : message,
            error?.ErrorCode,
            error?.RequestId);
    }

    private static string Fallback(string attempted, long storeId, IApiResponse response)
        => $"Adyen refused to {attempted} for store {storeId} "
           + $"({(int)response.StatusCode} {response.ReasonPhrase}).";

    private static string? Truncate(string? content)
        => content is null || content.Length <= MaxProviderDetailLength
            ? content
            : content[..MaxProviderDetailLength];
}
