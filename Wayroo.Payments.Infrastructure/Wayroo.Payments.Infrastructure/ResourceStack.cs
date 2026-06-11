using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SNS;
using Constructs;
using Wayroo.Payments.Infrastructure.Resources;

namespace Wayroo.Payments.Infrastructure;

internal class ResourceStack : Stack
{
    private readonly InfrastructureResources _resource;

    internal ResourceStack(
        Construct scope,
        string id,
        IStackProps stackProps) : base(scope, id, stackProps)
    {
        var config = InitializeParameters();
        _resource = CreateInfrastructureResources(config);
        ApplyTags(config);
    }

    private void ApplyTags(StackConfig config)
    {
    }

    private InfrastructureResources CreateInfrastructureResources(StackConfig config)
    {
        var alarmTopic = Topic.FromTopicArn(this, id: "AlarmRelayTopic", config.AlarmTopicArn);

        var artifactsBucket = Bucket.FromBucketArn(
            this,
            "ArtifactsBucket",
            config.ArtifactsBucketArn);

        // The recorder lambda needs to live in the Wayroo VPC so it can resolve internal hostnames
        // like orders.luci-{env} when looking up the store/tenant for an account. Mirrors how the
        // Notification recorder lambda is wired (Wayroo.Notification.Infrastructure/ResourceStack.cs).
        var wayrooVpc = Vpc.FromVpcAttributes(this, id: "WayrooVPC", new VpcAttributes
        {
            VpcId = config.WayrooVpcId,
            AvailabilityZones = config.WayrooAvailabilityZones,
            PrivateSubnetIds = config.WayrooSubnetIds,
        });

        // The default table name comes from the DataAccess options, matching how the lambda resolves
        // it at runtime; the table itself is named "{environment}-{tableName}".
        var dbClientOptions = new DataAccess.DynamoDbClientOptions();
        var configurationTable = new PaymentConfigurationTable(
            this,
            id: "PaymentConfigurationTable",
            config.Environment,
            dbClientOptions.PaymentConfigurationTableName);

        return new InfrastructureResources
        {
            PaymentConfigurationTable = configurationTable,
            ConfigurationRecorderLambda = new ConfigurationRecorderLambda(
                this,
                id: "ConfigurationRecorderLambda",
                environment: config.Environment,
                artifactsBucket: artifactsBucket,
                functionVersion: config.LambdaArtifactVersion,
                alarmTopic: alarmTopic,
                configurationTable: configurationTable,
                vpc: wayrooVpc,
                webhookEventBusArn: config.WebhookEventBusArn),
            PaymentsAPI = new PaymentsAPI(
                this,
                environment: config.Environment,
                wayrooVpc: wayrooVpc,
                wayrooECSSecurityGroupId: config.WayrooECSSecurityGroupId,
                cloudMapNamespaceId: config.CloudMapNamespaceId,
                cloudMapNamespaceArn: config.CloudMapNamespaceArn,
                configurationTable: configurationTable),
        };
    }

    private StackConfig InitializeParameters()
    {
        var alarmTopicArn = new CfnParameter(
            this,
            id: "AlarmRelayTopicArn",
            new CfnParameterProps
            {
                Type = "String",
                Description =
                    "The ARN of the SNS Topic where alarms from this service component should be routed.",
                MinLength = 1, // force this to be supplied
            }
        ).ValueAsString;

        var environment = new CfnParameter(
            this,
            id: "Environment",
            new CfnParameterProps
            {
                Type = "String",
                Description =
                    "The environment name this stack is being deployed to. [dev, qa, prod].",
                MinLength = 1, // force this to be supplied
            }
        ).ValueAsString;

        var artifactsBucketArn = new CfnParameter(
            this,
            id: "ArtifactsBucketArn",
            new CfnParameterProps
            {
                Type = "String",
                Description =
                    "The ARN of the Amazon S3 bucket where code artifacts are deployed for resource use.",
                MinLength = 1, // force this to be supplied
            }
        ).ValueAsString;

        var lambdaArtifactVersion = new CfnParameter(
            this,
            id: "LambdaArtifactVersion",
            new CfnParameterProps
            {
                Type = "String",
                Description =
                    "The version number to use when retrieving the associated code artifacts for resource use.",
                MinLength = 1, // force this to be supplied
            }
        ).ValueAsString;

        // NOTE: the Orders API URL is not a CFN parameter — the lambda loads it at runtime from SSM
        // Parameter Store at /luci/services/utility/OrdersClientOptions/ApiBaseUrl. Each environment's
        // value lives in SSM and the deploy pipeline doesn't need to plumb it through.

        // VPC parameters — the recorder lambda runs inside the Wayroo VPC so it can hit internal
        // hostnames (Orders). Values come from the env-specific pipeline variables files.
        var wayrooVpcId = new CfnParameter(
            this,
            id: "WayrooVPCId",
            new CfnParameterProps
            {
                Type = "String",
                Description = "The ID of the VPC which contains the Wayroo APIs.",
                MinLength = 1,
            }
        ).ValueAsString;

        var wayrooAvailabilityZones = new CfnParameter(
            this,
            id: "WayrooAvailabilityZones",
            new CfnParameterProps
            {
                Type = "List<String>",
                Description =
                    "The availability zones the VPC subnets live in (must match the supplied subnets).",
                MinLength = 1,
            }
        ).ValueAsList;

        var wayrooSubnetIds = new CfnParameter(
            this,
            id: "WayrooVPCSubnetIds",
            new CfnParameterProps
            {
                Type = "List<String>",
                Description = "The IDs of the private subnets the lambda's ENIs should be attached to.",
                MinLength = 1,
            }
        ).ValueAsList;

        var webhookEventBusArn = new CfnParameter(
            this,
            id: "WebhookEventBusArn",
            new CfnParameterProps
            {
                Type = "String",
                Description =
                    "ARN of the environment's webhook event bus (provisioned by another stack). The recorder lambda's source queue subscribes to events on this bus.",
                MinLength = 1,
            }
        ).ValueAsString;

        // ECS / Cloud Map parameters for the API construct. Mirrors Wayroo.Notification's stack —
        // the API runs on the shared {env}-ecs-cluster, in the Wayroo ECS security group, registering
        // service discovery in the luci-{env} Cloud Map namespace.
        var wayrooECSSecurityGroupId = new CfnParameter(
            this,
            id: "WayrooECSSecurityGroupId",
            new CfnParameterProps
            {
                Type = "String",
                Description = "The ID of the security group the API tasks run in.",
                MinLength = 1,
            }
        ).ValueAsString;

        var cloudMapNamespaceId = new CfnParameter(
            this,
            id: "CloudMapNamespaceId",
            new CfnParameterProps
            {
                Type = "String",
                Description = "The ID of the AWS Cloud Map Namespace the API is registered into.",
                MinLength = 1,
            }
        ).ValueAsString;

        // The Cloud Map ARN is required alongside the ID — the .NET CDK can't derive one from the
        // other when importing the namespace. Same caveat applies in Wayroo.Notification.
        var cloudMapNamespaceArn = new CfnParameter(
            this,
            id: "CloudMapNamespaceArn",
            new CfnParameterProps
            {
                Type = "String",
                Description = "The ARN of the AWS Cloud Map Namespace the API is registered into.",
                MinLength = 1,
            }
        ).ValueAsString;

        return new StackConfig
        {
            Environment = environment,
            AlarmTopicArn = alarmTopicArn,
            ArtifactsBucketArn = artifactsBucketArn,
            LambdaArtifactVersion = lambdaArtifactVersion,
            WayrooVpcId = wayrooVpcId,
            WayrooAvailabilityZones = wayrooAvailabilityZones,
            WayrooSubnetIds = wayrooSubnetIds,
            WebhookEventBusArn = webhookEventBusArn,
            WayrooECSSecurityGroupId = wayrooECSSecurityGroupId,
            CloudMapNamespaceId = cloudMapNamespaceId,
            CloudMapNamespaceArn = cloudMapNamespaceArn,
        };
    }
}
