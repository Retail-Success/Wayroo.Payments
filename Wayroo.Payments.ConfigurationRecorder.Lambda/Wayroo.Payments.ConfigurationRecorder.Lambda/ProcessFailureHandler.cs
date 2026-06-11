using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Wayroo.Common.Exceptions;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

/// <summary>
/// Decides what to do with a message whose processing threw. Mirrors the failure-handling pattern in
/// Wayroo.Orders.Lambda.PropayReportParser:
/// <list type="bullet">
///   <item><see cref="ResourceConflictException"/> — duplicate; discard (the message acks normally).</item>
///   <item><see cref="ResourceAccessException"/> — transient / not-yet-resolvable; re-queue to the source
///   queue (resetting the receive count and retention clock) so it retries indefinitely.</item>
///   <item>anything else — unrecoverable; send to the dead-letter queue.</item>
/// </list>
/// </summary>
public class ProcessFailureHandler(
    ILogger<ProcessFailureHandler> logger,
    IAmazonSQS client,
    string deadLetterQueueUrl,
    string sourceQueueUrl)
{
    // Delay before a re-queued message becomes visible again, so retries (e.g. waiting on store
    // onboarding) don't hot-loop and burn invocations.
    private const int ReQueueDelaySeconds = 60;

    public async Task HandleFailure(SQSEvent.SQSMessage message, Exception failure)
    {
        using (logger.BeginScope("{MessageId}", message.MessageId))
        {
            if (failure is ResourceConflictException)
            {
                logger.LogWarning(failure, "Resource conflict processing message; assuming duplicate. Discarding.");
                return;
            }

            if (failure is ResourceAccessException)
            {
                logger.LogWarning(failure, "Resource not yet resolvable processing message. Re-queueing to retry.");
                await SendMessage(message, sourceQueueUrl, ReQueueDelaySeconds);
                return;
            }

            logger.LogError(failure, "Unrecoverable error processing message. Dead-lettering.");
            await SendMessage(message, deadLetterQueueUrl, delaySeconds: 0);
        }
    }

    private async Task SendMessage(SQSEvent.SQSMessage message, string queueUrl, int delaySeconds)
    {
        var request = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            DelaySeconds = delaySeconds,
            MessageAttributes = message.MessageAttributes?.ToDictionary(
                entry => entry.Key,
                entry => new MessageAttributeValue
                {
                    DataType = entry.Value.DataType,
                    StringListValues = entry.Value.StringListValues,
                    StringValue = entry.Value.StringValue,
                }),
            MessageBody = message.Body,
        };

        await client.SendMessageAsync(request);
    }
}
