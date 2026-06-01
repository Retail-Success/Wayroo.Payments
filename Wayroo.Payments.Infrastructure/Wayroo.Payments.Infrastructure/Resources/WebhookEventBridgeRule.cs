using Amazon.CDK;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.SQS;
using Constructs;
using EventBus = Amazon.CDK.AWS.Events.EventBus;
using SqsQueue = Amazon.CDK.AWS.Events.Targets.SqsQueue;
using Function = Wayroo.Payments.ConfigurationRecorder.Lambda.Function;

namespace Wayroo.Payments.Infrastructure.Resources;

/// <summary>
/// Routes every event on the environment's webhook event bus into the recorder lambda's source queue.
/// </summary>
/// <remarks>
/// The webhook bus (<c>{env}-webhook-bus</c>) is the entry point for provider webhook traffic that
/// originates outside this stack — e.g. MerchantWare / ProPay credential events. We import it by
/// ARN (the bus is owned by another stack) and attach a rule that has no detail-type or source
/// filter on purpose: today we don't know the canonical shape of the events we'll receive, so we
/// take everything and let the recorder's parser reject anything it can't handle. When the upstream
/// publisher's event shape stabilises, narrow this pattern down (likely a <c>source</c> match on the
/// webhook ingester and/or <c>detail-type</c> match on the credential event type).
/// <para>
/// CDK auto-attaches an SQS resource policy to the queue granting <c>events.amazonaws.com</c>
/// permission to <c>sqs:SendMessage</c> on it as a side effect of declaring the queue as a Rule
/// target — no manual queue policy work needed here.
/// </para>
/// </remarks>
internal class WebhookEventBridgeRule
{
    public IRule Resource { get; }

    internal WebhookEventBridgeRule(Construct scope, string id, string environment, string eventBusArn, IQueue targetQueue)
    {
        var eventBus = EventBus.FromEventBusArn(scope, "WebhookEventBus", eventBusArn);

        Resource = new Rule(scope, id, new RuleProps
        {
            RuleName = $"{environment}-{Function.ServiceName}-{Function.ComponentName}-WebhookEvents",
            Description =
                "Forwards every event published to the environment's webhook event bus into the payment configuration recorder lambda's source queue.",
            EventBus = eventBus,
            // Catch-all pattern: any event on this bus that originated from this AWS account. Empty
            // EventPattern isn't valid in CloudFormation, and an account-scoped match is the lightest
            // catch-all that still satisfies the requirement that the pattern be specified.
            EventPattern = new EventPattern
            {
                Account = new[] { Stack.Of(scope).Account },
            },
            Targets = new IRuleTarget[]
            {
                new SqsQueue(targetQueue),
            },
        });
    }
}
