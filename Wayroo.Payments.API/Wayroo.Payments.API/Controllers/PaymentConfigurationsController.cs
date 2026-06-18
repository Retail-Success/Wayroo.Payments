using Microsoft.AspNetCore.Mvc;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.API.Controllers;

/// <summary>
/// Read-only access to recorded payment-provider configurations. Writes flow exclusively through
/// the SQS-triggered <c>Wayroo.Payments.ConfigurationRecorder.Lambda</c>.
/// </summary>
[ApiController]
[Route(Routes.ConfigurationsRoute)]
[ApiVersion(Routes.MinimumSupportedVersionString)]
public class PaymentConfigurationsController(
    ILogger<PaymentConfigurationsController> logger,
    IPaymentConfigurationRepository repository) : ControllerBase
{
    /// <summary>
    /// Lists every payment-provider configuration recorded for the store.
    /// </summary>
    /// <param name="storeId">The store identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet(Name = nameof(GetConfigurationsForStore))]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentProviderConfiguration>), 200)]
    public async Task<ActionResult<IReadOnlyList<PaymentProviderConfiguration>>> GetConfigurationsForStore(
        [FromRoute] long storeId,
        CancellationToken cancellationToken)
    {
        var configurations = await repository.GetConfigurationsForStore(storeId, cancellationToken);
        logger.LogInformation(
            "Retrieved {Count} payment configurations for store {StoreId}",
            configurations.Count,
            storeId);
        return Ok(configurations);
    }

    /// <summary>
    /// Retrieves the configuration for the store + provider. Returns 404 when no configuration has been recorded.
    /// </summary>
    /// <param name="storeId">The store identifier.</param>
    /// <param name="providerId">The payment provider identifier (e.g. <c>propay</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{providerId}", Name = nameof(GetConfiguration))]
    [ProducesResponseType(typeof(PaymentProviderConfiguration), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<PaymentProviderConfiguration>> GetConfiguration(
        [FromRoute] long storeId,
        [FromRoute] string providerId,
        CancellationToken cancellationToken)
    {
        var configuration = await repository.GetConfiguration(storeId, providerId, cancellationToken);
        if (configuration is null)
        {
            return NotFound();
        }

        return Ok(configuration);
    }
}
