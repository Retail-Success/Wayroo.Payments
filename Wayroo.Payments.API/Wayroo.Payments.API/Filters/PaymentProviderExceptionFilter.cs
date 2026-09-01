using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Wayroo.Payments.BusinessLogic.Gateways;

namespace Wayroo.Payments.API.Filters;

/// <summary>
/// Maps the two payment failures a caller can do something about onto status codes, and leaves
/// everything else alone.
/// </summary>
/// <remarks>
/// <para>
/// A provider answering "no" is not a fault in this service, and a caller retrying it will get the
/// same answer — so it must not read as a <c>500</c>. Luci.Orders' <c>StorePropayController</c> maps
/// the equivalent <c>PropayException</c> onto a validation problem in the same way, which keeps this
/// class of response familiar to callers migrating over.
/// </para>
/// <para>
/// Every other exception is left alone, so transport failures and bugs still surface as <c>500</c>
/// and still reach the logs as unhandled.
/// </para>
/// </remarks>
public class PaymentProviderExceptionFilter(ILogger<PaymentProviderExceptionFilter> logger) : IExceptionFilter
{
    /// <inheritdoc />
    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case PaymentProviderException failure:
                logger.LogWarning(
                    failure,
                    "The payment provider refused the request (provider status {ProviderStatusCode}).",
                    failure.ProviderStatusCode);
                Respond(context, StatusCodes.Status400BadRequest, BuildProviderErrors(failure));
                break;

            // The caller asked for a provider we cannot reach — their input, so their 400.
            case PaymentProviderNotSupportedException unsupported:
                logger.LogWarning(
                    "A caller asked for provider {RequestedProviderId}, which has no gateway.",
                    unsupported.RequestedProviderId);
                Respond(
                    context,
                    StatusCodes.Status400BadRequest,
                    new Dictionary<string, string[]>
                    {
                        ["providerId"] = [unsupported.Message],
                    });
                break;

            // The request is well-formed; the stored state is not. Answered as a conflict because it
            // takes an operator to settle which provider a store is on — a retry will not.
            case PaymentProviderAmbiguousException ambiguous:
                logger.LogError(
                    ambiguous,
                    "Could not determine the payment provider for store {StoreId} (candidates: {CandidateProviderIds}).",
                    ambiguous.StoreId,
                    ambiguous.CandidateProviderIds);
                Respond(context, StatusCodes.Status409Conflict, BuildAmbiguityErrors(ambiguous));
                break;
        }
    }

    private static Dictionary<string, string[]> BuildProviderErrors(PaymentProviderException failure)
    {
        var errors = new Dictionary<string, string[]> { [string.Empty] = [failure.Message] };

        if (!string.IsNullOrWhiteSpace(failure.Details))
            errors["Details"] = [failure.Details];

        if (!string.IsNullOrWhiteSpace(failure.ProviderStatusCode))
            errors["ProviderStatusCode"] = [failure.ProviderStatusCode];

        return errors;
    }

    private static Dictionary<string, string[]> BuildAmbiguityErrors(PaymentProviderAmbiguousException ambiguous)
    {
        var errors = new Dictionary<string, string[]> { [string.Empty] = [ambiguous.Message] };

        if (ambiguous.CandidateProviderIds.Count > 0)
            errors["CandidateProviderIds"] = [.. ambiguous.CandidateProviderIds];

        return errors;
    }

    private static void Respond(ExceptionContext context, int statusCode, Dictionary<string, string[]> errors)
    {
        context.Result = new ObjectResult(new ValidationProblemDetails(errors)
        {
            Status = statusCode,
            Instance = context.HttpContext.Request.Path,
        })
        {
            StatusCode = statusCode,
        };

        context.ExceptionHandled = true;
    }
}
