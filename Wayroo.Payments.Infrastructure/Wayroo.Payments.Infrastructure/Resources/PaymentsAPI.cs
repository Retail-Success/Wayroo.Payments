using Amazon.CDK;
using Amazon.CDK.AWS.ApplicationAutoScaling;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.ServiceDiscovery;
using Amazon.CDK.AWS.SNS;
using Constructs;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.Infrastructure.Resources;

/// <summary>
/// ECS Fargate deployment of <c>Wayroo.Payments.API</c>. Mirrors the
/// <c>Wayroo.Notification.Infrastructure/Resources/NotificationAPI.cs</c> construct so the two
/// Wayroo micros stay aligned: shared ECS cluster, app + X-Ray OTEL sidecar containers, Cloud Map
/// service discovery (resolves as <c>payments.luci-{env}</c>), CPU/memory auto-scaling.
/// </summary>
internal class PaymentsAPI
{
    private const string ECRRepositoryName = "wayroo.payments";
    private const string ECRAccountId = "447351046706";
    private const string ECRRegion = "us-east-1";

    // Mirror Function.ServiceName / ComponentName in the recorder lambda so alarm names across this
    // service read the same way: {env}-WayrooPayments-{Component}-{What}. Hardcoded for the same
    // reason the environment variable keys below are — see the ProjectReference comment in this
    // project's csproj for why the API is not referenced from here.
    private const string ServiceName = "WayrooPayments";
    private const string ComponentName = "API";

    /// <summary>
    /// Namespace for metrics extracted from this service's ECS logs. Parallels the <c>RS/lambda/</c>
    /// namespace every lambda log metric in the Wayroo services publishes to; this is the first ECS
    /// one, hence a new namespace rather than reusing that.
    /// </summary>
    private const string LogMetricNamespace = "RS/ecs/";

    /// <summary>
    /// How many occurrences of a signal within <see cref="SignalPeriodMinutes"/> are tolerated before
    /// the alarm fires, and for how many consecutive periods.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both of these signals are ordinary in ones and twos</b> — that is why they are logged at
    /// Information and why the threshold is not zero the way the lambda's error alarms are. A store
    /// that never onboarded genuinely has no account, and a store the backfill has not reached
    /// genuinely has no recorded standing. Alarming on a single occurrence would page someone
    /// throughout the backfill rollout and teach them to ignore it.
    /// </para>
    /// <para>
    /// What is <i>not</i> ordinary is a sustained rate. These values say: more than
    /// <see cref="SignalThreshold"/> in a <see cref="SignalPeriodMinutes"/>-minute window, for
    /// <see cref="SignalEvaluationPeriods"/> consecutive windows. Starting values, chosen to sit above
    /// incidental traffic and below a genuine flood, with no production rate to calibrate against yet
    /// — revisit once the backfill has run and the steady-state rate is known. The metrics are
    /// published either way, so the history needed to tune them accumulates from the first deploy.
    /// </para>
    /// </remarks>
    private const int SignalThreshold = 10;

    /// <inheritdoc cref="SignalThreshold" />
    private const int SignalPeriodMinutes = 5;

    /// <inheritdoc cref="SignalThreshold" />
    private const int SignalEvaluationPeriods = 2;

    public PaymentsAPI(
        Construct scope,
        string environment,
        IVpc wayrooVpc,
        string wayrooECSSecurityGroupId,
        string cloudMapNamespaceId,
        string cloudMapNamespaceArn,
        PaymentConfigurationTable configurationTable,
        ITopic alarmTopic,
        string propayRestBaseUri,
        string propayXmlBaseUri,
        string protectPayRestBaseUri)
    {
        // Existing shared ECS cluster — provisioned by infra; we just attach to it.
        var ecsCluster = Cluster.FromClusterAttributes(scope, id: "EcsCluster", new ClusterAttributes
        {
            ClusterName = $"{environment}-ecs-cluster",
            Vpc = wayrooVpc,
        });

        // Imported roles (Mutable = false; CDK never modifies them).
        //
        // The task role MUST already exist with:
        //   - dynamodb:GetItem, dynamodb:Query on the {env}-PaymentConfiguration table
        //   - dynamodb:UpdateItem on the same table — both writers merge per attribute rather than
        //     putting whole items, so the account refresh needs UpdateItem, not PutItem
        //   - kms:Decrypt on the table's customer-managed key (if the table uses one)
        //   - ssm:GetParametersByPath on the shared ProPay vendor path (PropaySecretsPath below) and
        //     kms:Decrypt on the key those SecureString parameters are encrypted with. Without these
        //     the account endpoints fail at startup, because that configuration source is required.
        //   - cloudwatch:PutMetricData (for OpenTelemetry metrics export)
        //   - xray:PutTraceSegments, xray:PutTelemetryRecords (X-Ray sidecar)
        var taskRole = Role.FromRoleName(
            scope,
            id: "PaymentsAPITaskRole",
            roleName: $"{environment}-payments-service-role",
            new FromRoleNameOptions { Mutable = false });
        var executionRole = Role.FromRoleName(
            scope,
            id: "PaymentsAPIExecutionRole",
            roleName: "ecs-execution-role",
            new FromRoleNameOptions { Mutable = false });

        var ecsSecurityGroup = SecurityGroup.FromSecurityGroupId(
            scope,
            id: "PaymentsAPISecurityGroup",
            securityGroupId: wayrooECSSecurityGroupId,
            new SecurityGroupImportOptions { Mutable = false });

        // ECR image URI pinned by environment tag (CI/CD pushes wayroo.payments:{env} on each deploy).
        var ecrImageUri = $"{ECRAccountId}.dkr.ecr.{ECRRegion}.amazonaws.com/{ECRRepositoryName}:{environment}";
        var serviceName = $"{environment}-wayroo-payments";

        var logGroup = new LogGroup(scope, id: "PaymentsAPILogGroup", new LogGroupProps
        {
            LogGroupClass = LogGroupClass.STANDARD,
            LogGroupName = $"/ecs/{serviceName}",
            Retention = RetentionDays.ONE_MONTH,
            RemovalPolicy = RemovalPolicy.RETAIN,
        });

        var taskDefinition = new FargateTaskDefinition(scope, id: "PaymentsAPITaskDef", new FargateTaskDefinitionProps
        {
            Family = serviceName,
            Cpu = 256,
            MemoryLimitMiB = 512,
            TaskRole = taskRole,
            ExecutionRole = executionRole,
        });

        // X-Ray / OTEL collector sidecar — receives OTLP gRPC from the app container on :4317 and
        // forwards to AWS X-Ray + CloudWatch.
        var xrayContainer = taskDefinition.AddContainer(id: "xray-otel", new ContainerDefinitionProps
        {
            Image = ContainerImage.FromRegistry("public.ecr.aws/aws-observability/aws-otel-collector:latest"),
            Command =
            [
                "--config=/etc/ecs/container-insights/otel-task-metrics-config.yaml",
            ],
            PortMappings = [new PortMapping { ContainerPort = 4317, HostPort = 4317, Protocol = Amazon.CDK.AWS.ECS.Protocol.UDP }],
        });

        var appContainer = taskDefinition.AddContainer(id: "app", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromRegistry(ecrImageUri),
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps
            {
                StreamPrefix = serviceName,
                LogGroup = logGroup,
            }),
            // Keys mirror Wayroo.Payments.API.EnvironmentVariableKeys. The API is not referenced from
            // this project (it is a web project; see the csproj), so they're inlined; keep them in
            // sync with that file. The
            // `PaymentConfigurationTableName` key mirrors Wayroo.Payments.DataAccess and matches the
            // recorder lambda's wiring — without it, the API uses the literal "PaymentConfiguration"
            // (no env prefix) and reads from a table that doesn't exist in deployed environments.
            Environment = new Dictionary<string, string>
            {
                ["AspNetCoreEnvironment"] = environment,
                ["Environment"] = environment,
                ["AwsRegion"] = "us-east-1",
                ["PaymentConfigurationTableName"] = configurationTable.Resource.TableName,
                // Parameter Store path holding the per-tenant ProPay credentials. Points at the vendor
                // path Luci.Orders already reads rather than a copy under /wayroo/api/payments: the
                // same credential in two places is how two services end up on different halves of a
                // rotation. A tenant's optional per-tenant base-URL override lives here too, and wins
                // over the environment defaults below for that tenant.
                ["PropaySecretsPath"] = $"/luci/{environment}/vendors/propay",
                // The environment-wide ProPay/ProtectPay endpoints — the same role
                // PropayApiBaseUrisOptions plays in Luci.Orders, and NOT something Parameter Store
                // holds. They come in as stack parameters because `environment` is a token here, so
                // each environment must supply its own literal through the pipeline variables.
                ["PropayApiBaseUrisOptions:PropayRest"] = propayRestBaseUri,
                ["PropayApiBaseUrisOptions:PropayXml"] = propayXmlBaseUri,
                ["PropayApiBaseUrisOptions:ProtectPayRest"] = protectPayRestBaseUri,
                ["OpenTelemetry:ServiceName"] = serviceName,
                ["OpenTelemetry:ServiceVersion"] = "1.0.0",
                ["OpenTelemetry:ExporterOtlpEndpoint"] = $"http://localhost:{xrayContainer.PortMappings.Single().ContainerPort}",
                ["OpenTelemetry:ExporterOtlpProtocol"] = "grpc",
                ["OpenTelemetry:TracesSampler"] = "always_on",
                ["OpenTelemetry:Propagators"] = "xray,tracecontext,baggage",
            },
            HealthCheck = new Amazon.CDK.AWS.ECS.HealthCheck
            {
                Command = new[] { "CMD-SHELL", "curl -f http://localhost:80/status || exit 1" },
                Interval = Duration.Seconds(30),
                Timeout = Duration.Seconds(5),
                Retries = 3,
                StartPeriod = Duration.Seconds(60),
            },
            Essential = true,
        });

        // Boot X-Ray collector before the app so app-startup traces flow through.
        appContainer.AddContainerDependencies(new ContainerDependency
        {
            Container = xrayContainer,
            Condition = ContainerDependencyCondition.START,
        });

        appContainer.AddPortMappings(new PortMapping
        {
            ContainerPort = 80,
            Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP,
        });

        // Cloud Map private DNS — service registers as `payments.luci-{env}`. Composites
        // (Luci.Management.Api) resolve {{Endpoints.Micro.WayrooPayments}} → http://payments.luci-{env}.
        var cloudMapNamespace = PrivateDnsNamespace.FromPrivateDnsNamespaceAttributes(
            scope,
            id: "WayrooCloudMapNamespace",
            new PrivateDnsNamespaceAttributes
            {
                NamespaceId = cloudMapNamespaceId,
                NamespaceArn = cloudMapNamespaceArn,
                NamespaceName = $"luci-{environment}",
            });

        var service = new FargateService(scope, id: "PaymentsAPIService", new FargateServiceProps
        {
            ServiceName = serviceName,
            Cluster = ecsCluster,
            TaskDefinition = taskDefinition,
            DesiredCount = 1, // auto-scaling takes over from here
            MinHealthyPercent = 50,
            MaxHealthyPercent = 200,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
            SecurityGroups = new[] { ecsSecurityGroup },
            EnableExecuteCommand = true,
            CircuitBreaker = new DeploymentCircuitBreaker { Rollback = true },
            HealthCheckGracePeriod = Duration.Seconds(60),
            CloudMapOptions = new CloudMapOptions
            {
                Name = "payments",
                DnsTtl = Duration.Seconds(30),
                FailureThreshold = 1,
                CloudMapNamespace = cloudMapNamespace,
            },
        });

        var scalingTarget = service.AutoScaleTaskCount(new EnableScalingProps
        {
            MinCapacity = 2,
            MaxCapacity = 4,
        });

        scalingTarget.ScaleOnCpuUtilization(id: "apiCpuScaling", new CpuUtilizationScalingProps
        {
            PolicyName = $"{serviceName}-CpuScaling",
            TargetUtilizationPercent = 50,
            ScaleInCooldown = Duration.Seconds(60),
            ScaleOutCooldown = Duration.Seconds(30),
        });
        scalingTarget.ScaleOnMemoryUtilization(id: "apiMemoryScaling", new MemoryUtilizationScalingProps
        {
            PolicyName = $"{serviceName}-MemoryScaling",
            TargetUtilizationPercent = 65,
            ScaleInCooldown = Duration.Seconds(60),
            ScaleOutCooldown = Duration.Seconds(30),
        });

        AddPaymentSignalAlarms(scope, environment, logGroup, alarmTopic);
    }

    /// <summary>
    /// Counts the two account-shaped log signals this service raises and alarms on a sustained rate of
    /// either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both alarms match on <c>$.Properties.PaymentsSignal</c> rather than on message text, so
    /// rewording a log line cannot silently break them. See
    /// <see cref="PaymentsLogSignals"/>, which both sides share.
    /// </para>
    /// <para>
    /// <b>A renamed signal fails silently in the safe-looking direction.</b> The filter would stop
    /// matching, the metric would report no data, and <c>TreatMissingData.NOT_BREACHING</c> reads no
    /// data as healthy — so the alarm sits green rather than going to INSUFFICIENT_DATA. That is the
    /// right setting here (an idle service legitimately emits neither signal) but it means these
    /// alarms cannot tell "nothing is wrong" from "nothing is being measured". If that distinction
    /// starts mattering, alarm on the ECS service's request count instead of on these.
    /// </para>
    /// </remarks>
    private static void AddPaymentSignalAlarms(
        Construct scope,
        string environment,
        LogGroup logGroup,
        ITopic alarmTopic)
    {
        // An account refresh that finds nothing is a 200 with accountExists: false, and one is normal.
        // A stream of them is not: it is the shape of a backfill that has stopped supplying
        // providerAccountRef, or one pointed at a tenant whose stores this service holds no references
        // for — both of which otherwise look like a clean run, because every store is "successfully"
        // recorded as having no account.
        AddSignalAlarm(
            scope,
            environment,
            logGroup,
            alarmTopic,
            signal: PaymentsLogSignals.RefreshAccountNoAccount,
            idPrefix: "PaymentsAPIRefreshAccountNoAccount",
            alarmNameSuffix: "RefreshAccount-NoAccount",
            alarmDescription:
                "Account refreshes are repeatedly finding no merchant account. Expected in ones and "
                + "twos for stores that never onboarded; a sustained rate points at a backfill that "
                + "is not supplying the provider account reference, or is sweeping the wrong tenant.");

        // A balance read that has to fetch the store's standing costs a second provider round trip and
        // then records it, so the store heals and the next read is one call again. That means this
        // should trend to zero as the backfill lands. A rate that does not fall means stores are not
        // healing, and the usual cause is the recording write failing rather than anything about the
        // read — which is logged as an Error separately, from PropayAccountGateway.
        AddSignalAlarm(
            scope,
            environment,
            logGroup,
            alarmTopic,
            signal: PaymentsLogSignals.GetBalanceStatusBackfilled,
            idPrefix: "PaymentsAPIGetBalanceStatusBackfilled",
            alarmNameSuffix: "GetBalance-StatusBackfilled",
            alarmDescription:
                "Balance reads are repeatedly having to fetch the account standing from the provider. "
                + "Each store should need this at most once and then stay healed, so a sustained rate "
                + "means the recording write is not sticking.");
    }

    private static void AddSignalAlarm(
        Construct scope,
        string environment,
        LogGroup logGroup,
        ITopic alarmTopic,
        string signal,
        string idPrefix,
        string alarmNameSuffix,
        string alarmDescription)
    {
        logGroup
            .AddMetricFilter(id: $"{idPrefix}Metric", new MetricFilterOptions
            {
                FilterName = signal,
                // Serilog's JSON formatter puts message template properties under "Properties", which
                // is why this is not a bare $.PaymentsSignal. The ECS awslogs driver forwards the
                // formatted line unchanged, so the log event is the JSON document.
                FilterPattern = FilterPattern.StringValue(
                    $"$.Properties.{PaymentsLogSignals.PropertyName}",
                    "=",
                    signal),
                Unit = Unit.COUNT,
                MetricName = $"{environment}-{ServiceName}-{ComponentName}-{signal}",
                MetricNamespace = LogMetricNamespace,
            })
            .Metric(new MetricOptions
            {
                Statistic = Stats.SUM,
                Period = Duration.Minutes(SignalPeriodMinutes),
            })
            .CreateAlarm(scope, id: $"{idPrefix}Alarm", new CreateAlarmOptions
            {
                AlarmName = $"{environment}-{ServiceName}-{ComponentName}-{alarmNameSuffix}",
                AlarmDescription = alarmDescription,
                ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
                Threshold = SignalThreshold,
                // Both periods must breach, so a single burst — one backfill batch, one support sweep —
                // does not page anyone. Sustained is the signal.
                EvaluationPeriods = SignalEvaluationPeriods,
                DatapointsToAlarm = SignalEvaluationPeriods,
                TreatMissingData = TreatMissingData.NOT_BREACHING,
                ActionsEnabled = true,
            })
            .AddAlarmAction(new SnsAction(alarmTopic));
    }
}
