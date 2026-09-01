using Amazon.DynamoDBv2.Model;

namespace Wayroo.Payments.DataAccess;

/// <summary>
/// The pieces of a DynamoDB <c>UpdateItem</c> call that describe a partial write to one payment
/// configuration record: which attributes to set, and the placeholder maps they are expressed with.
/// </summary>
/// <remarks>
/// A partial write rather than a whole-item put is what lets two independent writers — the webhook
/// recorder and the account refresh — share a record without either erasing the other's attributes.
/// Every attribute name is bound through <see cref="ExpressionAttributeNames"/> so that a name which
/// happens to be a DynamoDB reserved word can never break the expression.
/// </remarks>
/// <param name="UpdateExpression">The <c>SET</c> expression naming the attributes to write.</param>
/// <param name="ExpressionAttributeNames">Placeholder-to-attribute-name map.</param>
/// <param name="ExpressionAttributeValues">Placeholder-to-value map.</param>
public sealed record PaymentConfigurationUpdate(
    string UpdateExpression,
    Dictionary<string, string> ExpressionAttributeNames,
    Dictionary<string, AttributeValue> ExpressionAttributeValues);
