using Amazon.DynamoDBv2.Model;

namespace Wayroo.Payments.DataAccess.Extensions;

/// <summary>
/// Minimal helpers for converting between CLR values and DynamoDB <see cref="AttributeValue"/>s.
/// The payment configuration schema only uses strings and timestamps; this mirrors (in trimmed
/// form) the richer converters in Wayroo.ContentLibrary.DataAccess.
/// </summary>
public static class Converters
{
    public static AttributeValue ToAttributeValue(this string? value)
        => value is null ? new AttributeValue { NULL = true } : new AttributeValue { S = value };

    public static AttributeValue ToAttributeValue(this DateTimeOffset value)
        // Store as a round-trippable ISO 8601 string.
        => new() { S = value.ToString("O") };

    public static AttributeValue ToAttributeValue(this DateTimeOffset? value)
        => value is null ? new AttributeValue { NULL = true } : value.Value.ToAttributeValue();

    public static string? GetString(this Dictionary<string, AttributeValue> attributes, string attributeName)
        => attributes.TryGetValue(attributeName, out var attribute) ? attribute.S : null;

    public static DateTimeOffset? GetDateTimeOffset(this Dictionary<string, AttributeValue> attributes, string attributeName)
        => attributes.TryGetValue(attributeName, out var attribute) && DateTimeOffset.TryParse(attribute.S, out var value)
            ? value
            : null;
}
