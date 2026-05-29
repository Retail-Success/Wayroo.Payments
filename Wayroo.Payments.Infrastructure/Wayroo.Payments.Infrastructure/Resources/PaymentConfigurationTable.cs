using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Constructs;
using Wayroo.Payments.DataAccess;

namespace Wayroo.Payments.Infrastructure.Resources;

/// <summary>
/// The DynamoDB table storing payment provider configurations recorded by the processor lambda.
/// Mirrors the table conventions in Wayroo.ContentLibrary.Infrastructure (DSOContentTable).
/// Partitioned by store and sorted by provider.
/// </summary>
internal class PaymentConfigurationTable
{
    public ITable Resource { get; }

    internal PaymentConfigurationTable(Construct scope, string id, string environment, string tableName)
    {
        Resource = new Table(scope, id, new TableProps
        {
            TableName = $"{environment}-{tableName}",
            PartitionKey = new Amazon.CDK.AWS.DynamoDB.Attribute
            {
                Name = PaymentConfigurationSchemaProvider.AttributeNameForPartitionKey, // "StoreId"
                Type = AttributeType.NUMBER,
            },
            SortKey = new Amazon.CDK.AWS.DynamoDB.Attribute
            {
                Name = PaymentConfigurationSchemaProvider.AttributeNameForSortKey, // "ProviderId"
                Type = AttributeType.STRING,
            },
            BillingMode = BillingMode.PAY_PER_REQUEST,
            RemovalPolicy = RemovalPolicy.RETAIN,
            // TEMP: customer-managed KMS encryption disabled to unblock dev deploy — the deploying
            // CF role doesn't currently have kms:CreateKey, so provisioning a dedicated CMK was the
            // step the change set failed on. Without this line DynamoDB falls back to its default
            // AWS-owned encryption key (no KMS resource provisioned, no extra IAM needed). Records
            // still hold live payment credentials, so re-enable this with a real CMK once the role
            // has the needed permissions; remember to restore the kms:Decrypt / kms:GenerateDataKey
            // entries in the WorkerRole permissions comment over in ConfigurationRecorderLambda.
            // Encryption = TableEncryption.CUSTOMER_MANAGED,
            PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification
            {
                PointInTimeRecoveryEnabled = true,
                RecoveryPeriodInDays = 35, // Maximum allowed value
            },
            Stream = StreamViewType.NEW_AND_OLD_IMAGES,
        });
    }
}
