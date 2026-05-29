using Amazon.Lambda.SQSEvents;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

/// <summary>
/// Processes a single SQS message: resolves the store/tenant for the provider account and records the
/// configuration. Throws to signal failure; the <see cref="ProcessFailureHandler"/> decides what to do
/// with the message based on the exception type.
/// </summary>
public interface IMessageHandler
{
    Task Handle(SQSEvent.SQSMessage message, CancellationToken cancellationToken);
}
