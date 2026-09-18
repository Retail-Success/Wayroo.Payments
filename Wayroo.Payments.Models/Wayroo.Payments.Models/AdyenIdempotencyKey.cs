using System.Globalization;

namespace Wayroo.Payments.Models;

/// <summary>
/// The <c>Idempotency-Key</c> header sent with a create that must not happen twice.
/// </summary>
/// <remarks>
/// <para>
/// Derived from our own domain identity rather than generated, so a retry of the same logical
/// operation reproduces the key exactly — in a later process, after a redeploy, on another host.
/// A random key per attempt would be accepted by Adyen and protect nothing.
/// </para>
/// <para>
/// <b>Only the balance-platform rungs have one.</b> Legal Entity Management ignores the header
/// outright: the same key sent twice creates two legal entities. That is why this type offers a
/// factory per protected rung instead of one taking a step — a rung with no factory here is a rung
/// where persisted state is the only guard, and it should be impossible to believe otherwise.
/// </para>
/// <para>
/// <b>It is not the correctness guarantee even where it works.</b> Adyen expires keys after days,
/// and holds them at company-account level rather than per API credential — hence the leading
/// <c>wayroo</c>, so our keys cannot collide with another integration's. Reading the store id
/// straight out of a key in a log line or a support ticket is worth more here than the opacity a
/// UUID would buy.
/// </para>
/// </remarks>
public readonly struct AdyenIdempotencyKey : IEquatable<AdyenIdempotencyKey>
{
    /// <summary>The header Adyen reads the key from.</summary>
    public const string HeaderName = "Idempotency-Key";

    /// <summary>Adyen's own limit on the header, which every key produced here stays inside.</summary>
    public const int MaxLength = 64;

    private const string Namespace = "wayroo";
    private const string AccountHolderRung = "accountholder";
    private const string BalanceAccountRung = "balanceaccount";

    private readonly string _value;

    private AdyenIdempotencyKey(string value) => _value = value;

    /// <summary>The key for creating a store's account holder.</summary>
    /// <param name="environment">Which of our environments is calling.</param>
    /// <param name="storeId">The store being onboarded.</param>
    /// <param name="generation">The attempt, from <see cref="AdyenAccount.OnboardingGeneration"/>.</param>
    public static AdyenIdempotencyKey ForAccountHolder(
        AdyenReferenceEnvironment environment,
        long storeId,
        long generation)
        => Build(environment, storeId, AccountHolderRung, generation);

    /// <summary>The key for creating a store's balance account.</summary>
    /// <param name="environment">Which of our environments is calling.</param>
    /// <param name="storeId">The store being onboarded.</param>
    /// <param name="generation">The attempt, from <see cref="AdyenAccount.OnboardingGeneration"/>.</param>
    public static AdyenIdempotencyKey ForBalanceAccount(
        AdyenReferenceEnvironment environment,
        long storeId,
        long generation)
        => Build(environment, storeId, BalanceAccountRung, generation);

    /// <inheritdoc />
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc />
    public bool Equals(AdyenIdempotencyKey other)
        => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is AdyenIdempotencyKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value ?? string.Empty);

    public static bool operator ==(AdyenIdempotencyKey left, AdyenIdempotencyKey right) => left.Equals(right);

    public static bool operator !=(AdyenIdempotencyKey left, AdyenIdempotencyKey right) => !left.Equals(right);

    private static AdyenIdempotencyKey Build(
        AdyenReferenceEnvironment environment,
        long storeId,
        string rung,
        long generation)
    {
        if (storeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(storeId), storeId, "An idempotency key needs the store it is for.");

        // A generation below one would make a store's second attempt share a key with some other
        // attempt, which is the one thing this key exists to prevent.
        if (generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation), generation, "Onboarding generations start at one.");

        var value = string.Create(
            CultureInfo.InvariantCulture,
            $"{Namespace}-{Environment(environment)}-{storeId}-{rung}-g{generation}");

        // Asserted rather than assumed: every part but the store id and generation is fixed, so the
        // only way past the limit is an implausibly long identifier — and a key Adyen rejects would
        // fail the create it was meant to protect.
        return value.Length <= MaxLength
            ? new AdyenIdempotencyKey(value)
            : throw new ArgumentOutOfRangeException(
                nameof(storeId),
                storeId,
                $"The idempotency key '{value}' exceeds Adyen's {MaxLength}-character limit.");
    }

    // Dev and QA share one Adyen test platform, so this is what keeps their keys from colliding.
    private static string Environment(AdyenReferenceEnvironment environment) => environment switch
    {
        AdyenReferenceEnvironment.Dev => "dev",
        AdyenReferenceEnvironment.Qa => "qa",
        AdyenReferenceEnvironment.Prod => "prod",
        _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, "Unknown environment."),
    };
}
