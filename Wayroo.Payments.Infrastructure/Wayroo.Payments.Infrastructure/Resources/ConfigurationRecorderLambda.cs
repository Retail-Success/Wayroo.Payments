using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Lambda.EventSources;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SQS;
using Constructs;
using Wayroo.Payments.ConfigurationRecorder.Lambda;
using Function = Wayroo.Payments.ConfigurationRecorder.Lambda.Function;

namespace Wayroo.Payments.Infrastructure.Resources;

internal class ConfigurationRecorderLambda
{
    public readonly Queue Queue;
    public IFunction Resource { get; }
    public ILogGroup Logs { get; }
    public WebhookEventBridgeRule WebhookEventBridgeRule { get; }

    internal ConfigurationRecorderLambda(
        Construct scope,
        string id,
        string environment,
        IBucket artifactsBucket,
        string functionVersion,
        ITopic alarmTopic,
        PaymentConfigurationTable configurationTable,
        IVpc vpc,
        string webhookEventBusArn)
    {
        var functionName = $"{environment}-{Function.ServiceName}-{Function.ComponentName}";

        Logs = new LogGroup(scope, "ConfigurationRecorderLambdaLogs", new LogGroupProps
        {
            LogGroupClass = LogGroupClass.STANDARD,
            LogGroupName = $"{environment}-{Function.ServiceName}-{Function.ComponentName}",
            Retention = RetentionDays.TWO_WEEKS,
            RemovalPolicy = RemovalPolicy.RETAIN
        });

        var deadLetterQueue = new Queue(scope, "ConfigurationRecorderLambdaDLQ", new QueueProps
        {
            QueueName = $"{environment}-{Function.ServiceName}-{Function.ComponentName}-DLQ",
            RetentionPeriod = Duration.Days(14),
        });

        const int lambdaTimeoutSeconds = 5 * 60;
        Queue = new Queue(scope, "ConfigurationRecorderLambdaQueue", new QueueProps
        {
            QueueName = $"{environment}-{Function.ServiceName}-{Function.ComponentName}-Queue",
            VisibilityTimeout = Duration.Seconds(lambdaTimeoutSeconds * 6), // 6x the Lambda timeout to prevent reprocessing while it's being processed
            DeadLetterQueue = new DeadLetterQueue
            {
                Queue = deadLetterQueue,
                MaxReceiveCount = 3, // retries before moving to DLQ
            },
        });

        // Wire the environment's webhook event bus into the source queue. The rule has no
        // detail-type/source filter on purpose — the upstream event shape isn't pinned down yet,
        // so we take everything and let the recorder's parser handle the routing. See
        // WebhookEventBridgeRule.cs for the rationale and how to narrow the filter later.
        WebhookEventBridgeRule = new WebhookEventBridgeRule(
            scope,
            id: "WebhookEventBridgeRule",
            environment: environment,
            eventBusArn: webhookEventBusArn,
            targetQueue: Queue);

        var lambdaClassInfo = typeof(Function);
        var lambdaSourceObjectKey =
            $"{environment}/{Function.ServiceName}/{Function.ComponentName}/{functionVersion}/{Function.ServiceName}.{Function.ComponentName}.zip";

        var lambdaExecutionRole = Role.FromRoleName(
            scope,
            "ConfigurationRecorderLambdaServiceRole",
            roleName: $"{environment}/WorkerRole",
            new FromRoleNameOptions { Mutable = false });

        Resource = new Amazon.CDK.AWS.Lambda.Function(scope, id, new FunctionProps
        {
            FunctionName = functionName,
            Architecture = Architecture.X86_64,
            Runtime = Runtime.DOTNET_10,
            Handler = $"{lambdaClassInfo.Assembly.GetName().Name}::{lambdaClassInfo.FullName}::{nameof(Function.FunctionHandler)}",
            Description = "Records payment provider configurations received by Wayroo Payments.",
            Code = Code.FromBucketV2(
                artifactsBucket,
                lambdaSourceObjectKey),
            MemorySize = 512,
            Timeout = Duration.Seconds(lambdaTimeoutSeconds),
            Environment = new Dictionary<string, string>
            {
                [EnvironmentVariableKeys.PaymentConfigurationTableName] = configurationTable.Resource.TableName,
                [EnvironmentVariableKeys.AwsRegion] = "us-east-1",
                [EnvironmentVariableKeys.SourceQueueUrl] = Queue.QueueUrl,
                [EnvironmentVariableKeys.DeadLetterQueueUrl] = deadLetterQueue.QueueUrl,
                // The Orders API base URL is loaded by the lambda at runtime from SSM Parameter Store
                // at /luci/services/utility/OrdersClientOptions/ApiBaseUrl — not from an env var.
            },
            LogGroup = Logs,
            // Run inside the Wayroo VPC so the lambda can resolve internal hostnames (orders.luci-{env})
            // when looking up the store/tenant for a payment account. Without this, the lambda runs in
            // AWS-managed network space and DNS lookups for internal Wayroo APIs fail. Mirrors the
            // Notification recorder lambda's VPC wiring. CDK auto-creates a security group attached to
            // the lambda; no SecurityGroups override here lets that happen.
            Vpc = vpc,
            // While CDK can auto-create a role and policy aligning with the needs of this resource and the resources it's been configured to interact with,
            // to reduce the risks of having CloudFormation and deployments manage permissions, assume the role already exists with the necessary permissions.
            Role = lambdaExecutionRole
        });

        Resource.AddEventSource(new SqsEventSource(Queue, new SqsEventSourceProps
        {
            BatchSize = 5,
            // Functionally irrelevant for this recorder — FunctionHandler returns Task (not
            // Task<SQSBatchResponse>) and never emits batchItemFailures; ProcessFailureHandler does its
            // own per-message routing (re-queue → source, DLQ → dead-letter). Kept at `true` to match
            // the value the previous deploy provisioned on this event source mapping — flipping it
            // forces CloudFormation to call lambda:UpdateEventSourceMapping, which the deploying CF
            // role currently lacks. Once the role gains that permission, this can be set to false (or
            // removed) to reflect that we don't actually use partial-batch failure reporting.
            ReportBatchItemFailures = true
        }));
        Queue.GrantConsumeMessages(Resource);

        // The execution role is imported as immutable (see lambdaExecutionRole above), so CDK cannot
        // attach policies here — they must be present on the external "{env}/WorkerRole":
        //   - dynamodb:PutItem, GetItem, Query on the table
        //   - kms:Decrypt, kms:GenerateDataKey on the table's customer-managed key (writes use the CMK)
        //   - sqs:SendMessage on this queue (re-queue to retry) and its dead-letter queue
        //   - ssm:GetParametersByPath on "/luci/services/utility/OrdersClientOptions/*" — the lambda
        //     loads the Orders API base URL from SSM at
        //     /luci/services/utility/OrdersClientOptions/ApiBaseUrl on cold start
        //   - ec2:CreateNetworkInterface, ec2:DescribeNetworkInterfaces, ec2:DeleteNetworkInterface —
        //     required because Vpc is set; lambda provisions ENIs in the supplied subnets on cold start
        //     (the AWSLambdaVPCAccessExecutionRole managed policy covers these)
        //   - outbound network access to the Orders API (resolving store/tenant from the account number)

        ConfigureAlarms(
            scope,
            environment,
            alarmTopic,
            sourceQueue: Queue,
            deadLetterQueue: deadLetterQueue
        );
    }

    // <summary>
    // Establish Awareness to Operability Concerns
    // </summary>
    // <param name="scope"></param>
    // <param name="environment"></param>
    // <param name="alarmTopic"></param>
    // <param name="sourceQueue"></param>
    // <param name="deadLetterQueue"></param>
    private void ConfigureAlarms(Construct scope,
        string environment,
        ITopic alarmTopic,
        IQueue sourceQueue,
        IQueue deadLetterQueue)
    {
        // This alarm should cause an engineer to determine if a logic flaw or data shape change has
        // occurred requiring a fix and new deployment. ProcessFailureHandler routes anything that isn't
        // a ResourceConflict / ResourceAccess exception to the DLQ — any message landing here is by
        // definition outside the recorder's normal handling paths.
        deadLetterQueue.MetricApproximateNumberOfMessagesVisible()
            .CreateAlarm(
                scope,
                id: "ConfigurationRecorderLambdaDeadLettersExistAlarm",
                new CreateAlarmOptions
                {
                    AlarmName =
                        $"{environment}-{Function.ServiceName}-{Function.ComponentName}-DeadLetters-Exist",
                    AlarmDescription =
                        "Enable awareness to events which were unable to be processed and require attention.",
                    ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
                    DatapointsToAlarm = 1,
                    EvaluationPeriods = 1,
                    Threshold = 0,
                    TreatMissingData = TreatMissingData.NOT_BREACHING,
                    ActionsEnabled = true,
                }
            )
            .AddAlarmAction(new SnsAction(alarmTopic));

        // This alarm should cause an engineer to re-drive a message which was forgotten about after
        // fixing a previous dead letter alarm. Fires when the oldest message in the DLQ is at least a
        // day old — implies the original DeadLetters-Exist alarm went unacknowledged.
        deadLetterQueue.MetricApproximateAgeOfOldestMessage()
            .CreateAlarm(
                scope,
                id: "ConfigurationRecorderLambdaDeadLettersStillExistAlarm",
                new CreateAlarmOptions
                {
                    AlarmName =
                        $"{environment}-{Function.ServiceName}-{Function.ComponentName}-DeadLetters-StillExist",
                    AlarmDescription =
                        "Enable awareness to events which were unable to be processed and are at least a day old.",
                    ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
                    DatapointsToAlarm = 1,
                    EvaluationPeriods = 1,
                    Threshold = TimeSpan.FromDays(1).TotalSeconds,
                    TreatMissingData = TreatMissingData.NOT_BREACHING,
                    ActionsEnabled = true,
                }
            )
            .AddAlarmAction(new SnsAction(alarmTopic));

        // This alarm should cause an engineer to evaluate the performance of the lambda and consider
        // increasing its throughput or scaling. Fires when the oldest message in the source queue is
        // older than a minute — implies the lambda isn't keeping up with the incoming event rate.
        sourceQueue.MetricApproximateAgeOfOldestMessage()
            .CreateAlarm(
                scope,
                id: "ConfigurationRecorderLambdaSourceQueueThrottledAlarm",
                new CreateAlarmOptions
                {
                    AlarmName =
                        $"{environment}-{Function.ServiceName}-{Function.ComponentName}-Sources-Throttled",
                    AlarmDescription =
                        "Enable awareness to the lambda not keeping up with the volume coming in based on high age of message.",
                    ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
                    DatapointsToAlarm = 1,
                    EvaluationPeriods = 1,
                    // Expect the lambda to process messages within a minute of them being visible.
                    Threshold = TimeSpan.FromMinutes(1).TotalSeconds,
                    TreatMissingData = TreatMissingData.NOT_BREACHING,
                    ActionsEnabled = true,
                }
            )
            .AddAlarmAction(new SnsAction(alarmTopic));

        // This alarm should cause an engineer to evaluate the performance of the lambda and consider increasing its throughput or scaling.
        Resource.MetricErrors()
            .CreateAlarm(
                scope,
                id: "ConfigurationRecorderLambdaErrorAlarm",
                new CreateAlarmOptions
                {
                    AlarmName =
                        $"{environment}-{Function.ServiceName}-{Function.ComponentName}-Lambda-Error",
                    AlarmDescription =
                        "Enable awareness to the lambda failing unexpectedly; like a timeout.",
                    ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
                    DatapointsToAlarm = 1,
                    EvaluationPeriods = 1,
                    Threshold = 0,
                    TreatMissingData = TreatMissingData.NOT_BREACHING,
                    ActionsEnabled = true,
                }
            )
            .AddAlarmAction(new SnsAction(alarmTopic));

        // This alarm should cause an engineer to determine if a logic flaw or data shape change has occurred requiring a fix and new deployment.
        Logs.AddMetricFilter(
                id: "ConfigurationRecorderLambdaLogErrorMetric",
                new MetricFilterOptions
                {
                    FilterName = "Errors",
                    FilterPattern = FilterPattern.StringValue("$.Level", "=", "Error"),
                    Unit = Unit.COUNT,
                    MetricName =
                        $"{environment}-{Function.ServiceName}-{Function.ComponentName}-LogErrors",
                    MetricNamespace = "RS/lambda/",
                }
            )
            .Metric(new MetricOptions { Statistic = Stats.SUM, Period = Duration.Minutes(1) })
            .CreateAlarm(
                scope,
                id: "ConfigurationRecorderLambdaLogErrorAlarm",
                new CreateAlarmOptions
                {
                    AlarmName =
                        $"{environment}-{Function.ServiceName}-{Function.ComponentName}-Lambda-LogErrors",
                    AlarmDescription =
                        "Enable awareness to the lambda failing; like an unhandled exception.",
                    ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
                    DatapointsToAlarm = 1,
                    EvaluationPeriods = 1,
                    Threshold = 0,
                    TreatMissingData = TreatMissingData.NOT_BREACHING,
                    ActionsEnabled = true,
                }
            )
            .AddAlarmAction(new SnsAction(alarmTopic));
    }
}
