using System.Globalization;

namespace Wayroo.Payments.Messages;

/// <summary>
/// A monetary amount and the currency it is denominated in. Used by every payment event that
/// carries money, so that an amount can never travel without its currency.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Amount"/> is a <see cref="decimal"/>, never a <see cref="double"/> — binary floating
/// point cannot represent ordinary decimal fractions exactly, and a settlement pipeline that
/// reconciles to the penny cannot afford the drift.
/// </para>
/// <para>
/// Amounts are expressed in the major unit (<c>12.34</c> USD, not <c>1234</c> minor units), with
/// the sign describing direction relative to the account named by the event: positive means funds
/// moved <i>into</i> that account, negative means funds moved out. Each event's documentation
/// states the sign convention it uses.
/// </para>
/// <para>
/// <see cref="Currency"/> is an ISO 4217 alpha-3 code in uppercase. Build values through
/// <see cref="Of"/> or <see cref="Usd"/>, which normalize the code; the property itself is written
/// to the wire verbatim, and comparing <c>"usd"</c> with <c>"USD"</c> textually is exactly the kind
/// of silent mismatch the canonical wire conventions exist to prevent.
/// </para>
/// </remarks>
public sealed record Money
{
    /// <summary>The amount, in the currency's major unit. Signed; see the remarks on <see cref="Money"/>.</summary>
    public required decimal Amount { get; init; }

    /// <summary>The ISO 4217 alpha-3 currency code, uppercase, e.g. <c>"USD"</c>.</summary>
    public required string Currency { get; init; }

    /// <summary>Creates an amount in the given currency, normalizing the code to uppercase.</summary>
    /// <param name="amount">The amount, in the currency's major unit.</param>
    /// <param name="currency">An ISO 4217 alpha-3 code; case and surrounding whitespace are normalized.</param>
    /// <exception cref="ArgumentException">The currency code is blank.</exception>
    public static Money Of(decimal amount, string currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        string normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A currency code is required.", nameof(currency));
        }

        return new Money { Amount = amount, Currency = normalized };
    }

    /// <summary>Creates a US dollar amount.</summary>
    /// <param name="amount">The amount in dollars.</param>
    public static Money Usd(decimal amount) => new() { Amount = amount, Currency = "USD" };

    /// <summary>Renders the amount for logs and diagnostics, e.g. <c>"12.34 USD"</c>.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Amount} {Currency}");
}
