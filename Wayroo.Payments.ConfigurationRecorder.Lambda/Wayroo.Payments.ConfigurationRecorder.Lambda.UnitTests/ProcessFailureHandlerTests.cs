using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wayroo.Common.Exceptions;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.UnitTests;

public class ProcessFailureHandlerTests
{
    private const string SourceQueueUrl = "https://sqs.test/source";
    private const string DeadLetterQueueUrl = "https://sqs.test/dlq";

    private readonly Mock<IAmazonSQS> _sqs = new();

    private ProcessFailureHandler CreateHandler() => new(
        NullLogger<ProcessFailureHandler>.Instance,
        _sqs.Object,
        DeadLetterQueueUrl,
        SourceQueueUrl);

    private static SQSEvent.SQSMessage Message() => new()
    {
        MessageId = Guid.NewGuid().ToString(),
        Body = "{}",
    };

    [Fact]
    public async Task HandleFailure_ResourceConflict_DiscardsWithoutSending()
    {
        await CreateHandler().HandleFailure(Message(), new ResourceConflictException("duplicate"));

        _sqs.Verify(
            c => c.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleFailure_ResourceAccess_ReQueuesToSourceQueue()
    {
        SendMessageRequest? captured = null;
        _sqs
            .Setup(c => c.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new SendMessageResponse());

        await CreateHandler().HandleFailure(Message(), new ResourceAccessException("not resolvable yet"));

        captured.Should().NotBeNull();
        captured!.QueueUrl.Should().Be(SourceQueueUrl);
    }

    [Fact]
    public async Task HandleFailure_UnexpectedException_SendsToDeadLetterQueue()
    {
        SendMessageRequest? captured = null;
        _sqs
            .Setup(c => c.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new SendMessageResponse());

        await CreateHandler().HandleFailure(Message(), new InvalidOperationException("boom"));

        captured.Should().NotBeNull();
        captured!.QueueUrl.Should().Be(DeadLetterQueueUrl);
    }
}
