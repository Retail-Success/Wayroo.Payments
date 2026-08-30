using System.Globalization;

namespace Wayroo.Payments.Models;

/// <summary>
/// A monetary amount and the currency it is denominated in, so that an amount can never travel
/// without its currency.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Amount"/> is a <see cref="decimal"/>, never a <see cref="double"/> — binary floating
/// point cannot represent ordinary decimal fractions exactly. Amounts are expressed in the major
/// unit (<c>12.34</c> USD, not <c>1234</c> minor units); providers that report minor units are
/// converted at the gateway seam.
/// </para>
/// <para>
/// The wire shape matches <c>Wayroo.Payments.Messages.Money</c>. It is a separate type for the same
/// reason as <see cref="PaymentAccountStatus"/>: this package carries no package references.
/// </para>
/// </remarks>
public sealed record MoneyAmount
{
    /// <summary>The amount, in the currency's major unit.</summary>
    public required decimal Amount { get; init; }

    /// <summary>The ISO 4217 alpha-3 currency code, uppercase, e.g. <c>"USD"</c>.</summary>
    public required string Currency { get; init; }

    /// <summary>Creates an amount in the given currency, normalizing the code to uppercase.</summary>
    /// <param name="amount">The amount, in the currency's major unit.</param>
    /// <param name="currency">An ISO 4217 alpha-3 code; case and surrounding whitespace are normalized.</param>
    /// <exception cref="ArgumentException">The currency code is blank.</exception>
    public static MoneyAmount Of(decimal amount, string currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A currency code is required.", nameof(currency));
        }

        return new MoneyAmount { Amount = amount, Currency = normalized };
    }

    /// <summary>Creates a US dollar amount.</summary>
    /// <param name="amount">The amount in dollars.</param>
    public static MoneyAmount Usd(decimal amount) => new() { Amount = amount, Currency = "USD" };

    /// <summary>Renders the amount for logs and diagnostics, e.g. <c>"12.34 USD"</c>.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Amount} {Currency}");
}
