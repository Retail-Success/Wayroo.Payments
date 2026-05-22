using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Constructs;
using Wayroo.Payments.DataAccess;

namespace Wayroo.Payments.Infrastructure.Resources;

/// <summary>
/// The DynamoDB table storing payment provider configurations recorded by the processor lambda.
/// Mirrors the table conventions in Wayroo.ContentLibrary.Infrastructure (DSOContentTable).
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
                Name = PaymentConfigurationSchemaProvider.AttributeNameForPartitionKey, // "TenantId"
                Type = AttributeType.STRING,
            },
            SortKey = new Amazon.CDK.AWS.DynamoDB.Attribute
            {
                Name = PaymentConfigurationSchemaProvider.AttributeNameForSortKey, // "Provider"
                Type = AttributeType.STRING,
            },
            BillingMode = BillingMode.PAY_PER_REQUEST,
            RemovalPolicy = RemovalPolicy.RETAIN,
            PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification
            {
                PointInTimeRecoveryEnabled = true,
                RecoveryPeriodInDays = 35, // Maximum allowed value
            },
            Stream = StreamViewType.NEW_AND_OLD_IMAGES,
        });
    }
}
