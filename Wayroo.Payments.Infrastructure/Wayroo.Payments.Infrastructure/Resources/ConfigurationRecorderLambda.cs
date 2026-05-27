using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
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

    internal ConfigurationRecorderLambda(
        Construct scope,
        string id,
        string environment,
        IBucket artifactsBucket,
        string functionVersion,
        ITopic alarmTopic,
        PaymentConfigurationTable configurationTable)
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
            },
            LogGroup = Logs,
            // While CDK can auto-create a role and policy aligning with the needs of this resource and the resources it's been configured to interact with,
            // to reduce the risks of having CloudFormation and deployments manage permissions, assume the role already exists with the necessary permissions.
            Role = lambdaExecutionRole
        });

        Resource.AddEventSource(new SqsEventSource(Queue, new SqsEventSourceProps
        {
            BatchSize = 5,
            ReportBatchItemFailures = true
        }));
        Queue.GrantConsumeMessages(Resource);

        // The execution role is imported as immutable (see lambdaExecutionRole above), so CDK cannot
        // attach the table/KMS policies here — they must be present on the external "{env}/WorkerRole":
        //   - dynamodb:PutItem, GetItem, Query on the table
        //   - kms:Decrypt, kms:GenerateDataKey on the table's customer-managed key (writes use the CMK)

        ConfigureAlarms(
            scope,
            environment,
            alarmTopic
        );
    }

    // <summary>
    // Establish Awareness to Operability Concerns
    // </summary>
    // <param name="scope"></param>
    // <param name="environment"></param>
    // <param name="alarmTopic"></param>
    private void ConfigureAlarms(Construct scope,
        string environment,
        ITopic alarmTopic)
    {
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
