namespace Wayroo.Payments.SDK;

/// <summary>
/// Configuration consumed by <see cref="Extensions.IServiceCollectionExtensions.AddWayrooPaymentsClient"/>.
/// Bound from the <c>WayrooPaymentsClientOptions</c> configuration section.
/// </summary>
public class WayrooPaymentsClientOptions
{
    /// <summary>
    /// Absolute base URL of the Wayroo Payments API (no trailing slash). e.g.
    /// <c>http://localhost:8420</c> for local development.
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;
}
