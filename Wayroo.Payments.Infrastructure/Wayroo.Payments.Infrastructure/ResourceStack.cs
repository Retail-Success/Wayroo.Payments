using Amazon.CDK;
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
                ordersApiBaseUrl: config.OrdersApiBaseUrl)
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

        var ordersApiBaseUrl = new CfnParameter(
            this,
            id: "OrdersApiBaseUrl",
            new CfnParameterProps
            {
                Type = "String",
                Description =
                    "The absolute base URL of the Orders API used to resolve store/tenant from a ProPay account.",
                MinLength = 1, // force this to be supplied
            }
        ).ValueAsString;

        return new StackConfig
        {
            Environment = environment,
            AlarmTopicArn = alarmTopicArn,
            ArtifactsBucketArn = artifactsBucketArn,
            LambdaArtifactVersion = lambdaArtifactVersion,
            OrdersApiBaseUrl = ordersApiBaseUrl,
        };
    }
}
