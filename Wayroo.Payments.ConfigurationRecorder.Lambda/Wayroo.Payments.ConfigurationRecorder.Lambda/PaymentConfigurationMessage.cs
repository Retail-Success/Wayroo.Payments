namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

/// <summary>
/// The routing fields read from a queue message. The full message body is persisted verbatim as the
/// configuration payload; only the tenant and provider are needed to key the record.
/// </summary>
/// <remarks>
/// This contract is provisional and will be firmed up when the API Gateway -> SQS integration that
/// feeds this queue is designed.
/// </remarks>
public class PaymentConfigurationMessage
{
    public string? TenantId { get; set; }

    public string? Provider { get; set; }
}
