using Amazon.CDK;
using Amazon.CDK.AWS.ApplicationAutoScaling;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.ServiceDiscovery;
using Constructs;

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

    public PaymentsAPI(
        Construct scope,
        string environment,
        IVpc wayrooVpc,
        string wayrooECSSecurityGroupId,
        string cloudMapNamespaceId,
        string cloudMapNamespaceArn,
        PaymentConfigurationTable configurationTable)
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
        //   - kms:Decrypt on the table's customer-managed key (if the table uses one)
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
            // Keys mirror Wayroo.Payments.API.EnvironmentVariableKeys. Can't ProjectReference the API
            // (net8 vs net10 TFMs) so they're inlined; keep them in sync with that file. The
            // `PaymentConfigurationTableName` key mirrors Wayroo.Payments.DataAccess and matches the
            // recorder lambda's wiring — without it, the API uses the literal "PaymentConfiguration"
            // (no env prefix) and reads from a table that doesn't exist in deployed environments.
            Environment = new Dictionary<string, string>
            {
                ["AspNetCoreEnvironment"] = environment,
                ["Environment"] = environment,
                ["AwsRegion"] = "us-east-1",
                ["PaymentConfigurationTableName"] = configurationTable.Resource.TableName,
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
    }
}
