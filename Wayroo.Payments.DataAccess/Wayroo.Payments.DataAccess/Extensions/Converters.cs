using System.Globalization;
using Amazon.DynamoDBv2.Model;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.DataAccess.Extensions;

/// <summary>
/// Minimal helpers for converting between CLR values and DynamoDB <see cref="AttributeValue"/>s.
/// The payment configuration schema uses strings, numbers and timestamps; this mirrors (in trimmed
/// form) the richer converters in Wayroo.ContentLibrary.DataAccess.
/// </summary>
public static class Converters
{
    public static AttributeValue ToAttributeValue(this string? value)
        => value is null ? new AttributeValue { NULL = true } : new AttributeValue { S = value };

    public static AttributeValue ToAttributeValue(this long value)
        => new() { N = value.ToString(CultureInfo.InvariantCulture) };

    public static AttributeValue ToAttributeValue(this long? value)
        => value is null ? new AttributeValue { NULL = true } : value.Value.ToAttributeValue();

    public static AttributeValue ToAttributeValue(this Guid? value)
        => value is null ? new AttributeValue { NULL = true } : new AttributeValue { S = value.Value.ToString("D") };

    public static AttributeValue ToAttributeValue(this DateTimeOffset value)
        // Store as a round-trippable ISO 8601 string.
        => new() { S = value.ToString("O") };

    public static AttributeValue ToAttributeValue(this DateTimeOffset? value)
        => value is null ? new AttributeValue { NULL = true } : value.Value.ToAttributeValue();

    /// <summary>
    /// Stores a status by its <i>name</i>, not its numeric value, so that reordering the enum can
    /// never silently reinterpret already-persisted rows.
    /// </summary>
    public static AttributeValue ToAttributeValue(this PaymentAccountStatus? value)
        => value is null ? new AttributeValue { NULL = true } : new AttributeValue { S = value.Value.ToString() };

    public static string? GetString(this Dictionary<string, AttributeValue> attributes, string attributeName)
        => attributes.TryGetValue(attributeName, out var attribute) ? attribute.S : null;

    public static long? GetLong(this Dictionary<string, AttributeValue> attributes, string attributeName)
        => attributes.TryGetValue(attributeName, out var attribute)
           && long.TryParse(attribute.N, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>
    /// Reads an identifier written by <see cref="ToAttributeValue(Guid?)"/>.
    /// </summary>
    /// <remarks>
    /// Text that is not a well-formed identifier reads as <c>null</c> rather than throwing. The
    /// alternative would make one malformed attribute unreadable for the whole record, and the
    /// callers here treat an absent owner as an ordinary state.
    /// </remarks>
    public static Guid? GetGuid(this Dictionary<string, AttributeValue> attributes, string attributeName)
        => attributes.TryGetValue(attributeName, out var value)
           && value.S is not null
           && Guid.TryParse(value.S, out var parsed)
            ? parsed
            : null;

    public static DateTimeOffset? GetDateTimeOffset(this Dictionary<string, AttributeValue> attributes, string attributeName)
        => attributes.TryGetValue(attributeName, out var attribute) && DateTimeOffset.TryParse(attribute.S, out var value)
            ? value
            : null;

    /// <summary>
    /// Reads a status written by <see cref="ToAttributeValue(PaymentAccountStatus?)"/>. An
    /// unrecognised name reads as <c>null</c> rather than throwing, so a row written by a newer
    /// deployment cannot poison an older one.
    /// </summary>
    public static PaymentAccountStatus? GetPaymentAccountStatus(
        this Dictionary<string, AttributeValue> attributes,
        string attributeName)
        => attributes.TryGetValue(attributeName, out var attribute)
           && Enum.TryParse<PaymentAccountStatus>(attribute.S, ignoreCase: false, out var value)
            ? value
            : null;

    public static AttributeValue ToAttributeValue(this bool value)
        => new() { BOOL = value };

    public static bool? GetBool(this Dictionary<string, AttributeValue> attributes, string attributeName)
        => attributes.TryGetValue(attributeName, out var attribute) && attribute.IsBOOLSet
            ? attribute.BOOL
            : null;

    /// <summary>
    /// Reads an onboarding step written by name. An unrecognised name reads as
    /// <see cref="AdyenOnboardingStep.NotStarted"/> rather than throwing: a record written by a newer
    /// deployment must not poison an older one, and treating an unknown step as "no progress" makes a
    /// resumed onboarding re-check each rung rather than skip one it cannot account for.
    /// </summary>
    public static AdyenOnboardingStep GetAdyenOnboardingStep(
        this Dictionary<string, AttributeValue> attributes,
        string attributeName)
        => attributes.TryGetValue(attributeName, out var attribute)
           && Enum.TryParse<AdyenOnboardingStep>(attribute.S, ignoreCase: false, out var value)
            ? value
            : AdyenOnboardingStep.NotStarted;
}
