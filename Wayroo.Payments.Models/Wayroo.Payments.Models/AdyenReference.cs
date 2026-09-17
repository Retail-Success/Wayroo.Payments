using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Wayroo.Payments.Models;

/// <summary>
/// The handle we attach to an Adyen object so it can be traced back to a row in our system.
/// </summary>
/// <remarks>
/// <para>
/// Adyen assigns every object an opaque identifier we do not control. The <c>reference</c> field is
/// the only one we define — and for the objects onboarding creates, Adyen neither enforces uniqueness
/// on it nor indexes it for lookup, so the discipline is entirely ours to keep. References are
/// written once at creation and read for the life of the account, which is why the format has to
/// accommodate every scope up front rather than grow one later.
/// </para>
/// <para>
/// Shape: <c>&lt;env&gt;[&lt;scope&gt;&lt;id&gt;][_&lt;subject&gt;][_&lt;type&gt;]</c>. The scope
/// segment anchors the reference to a tenant (<c>t</c>) or to a person (<c>p</c>), or is omitted for
/// the platform itself. A trailing two-letter type segment names the kind of object; its absence
/// means a store, which is what references written before types existed look like.
/// </para>
/// <para>
/// <b>The separator is an underscore, and that is not cosmetic.</b> It is the only character accepted
/// by every <c>reference</c> field this format has to fit: a colon is fine on a legal entity, an
/// account holder and a balance account, and is rejected outright by a store and a merchant account.
/// One format across all of them has to take the intersection. Colons are still accepted when
/// <i>reading</i>, because the platform's own store identifiers keep theirs — only the value sent to
/// Adyen is transformed.
/// </para>
/// <para>
/// <b>Parse liberally, emit strictly, compare on values.</b> Both padded and unpadded spellings exist
/// in the wild (<c>Dt0004_s31234</c> alongside <c>Dt000004_s0031244</c>), so parsing accepts any digit
/// width, either separator and either case of hexadecimal, while <see cref="ToString"/> always emits
/// the canonical form. Equality is over the parsed values rather than the text, so two spellings of
/// one object compare equal — <b>never compare references as strings.</b>
/// </para>
/// </remarks>
public readonly struct AdyenReference : IEquatable<AdyenReference>
{
    /// <summary>
    /// The character separating a reference's segments.
    /// </summary>
    /// <remarks>
    /// An underscore because it is the only separator every <c>reference</c> field accepts — see the
    /// note on the type. A reference emitted with anything else is rejected on creation of a store or
    /// a merchant account, where the reference becomes the object's own identifier.
    /// </remarks>
    public const char Separator = '_';

    private const int TenantDigits = 6;
    private const int StoreDigits = 7;

    // Anchored, and deliberately permissive: any digit width, either separator, either case of
    // hexadecimal. See the parse-liberally note on the type.
    private static readonly Regex Pattern = new(
        @"^([DQP])(?:t(\d+)|p([0-9a-fA-F]{32}))?(?:[_:]s(\d+))?(?:[_:](le|ah|ba|st|ti|sc|ma|sw))?$",
        RegexOptions.CultureInvariant);

    private AdyenReference(
        AdyenReferenceEnvironment environment,
        AdyenReferenceScope scope,
        long? tenantId,
        Guid? personId,
        long? storeId,
        AdyenReferenceType type)
    {
        Environment = environment;
        Scope = scope;
        TenantId = tenantId;
        PersonId = personId;
        StoreId = storeId;
        Type = type;
    }

    /// <summary>Which of our environments the object belongs to.</summary>
    /// <remarks>
    /// Load-bearing rather than decorative: dev and QA share one Adyen test environment, so this
    /// letter is the only thing separating their records in a single pool of objects.
    /// </remarks>
    public AdyenReferenceEnvironment Environment { get; }

    /// <summary>Whether the reference is anchored to the platform, a tenant, or a person.</summary>
    public AdyenReferenceScope Scope { get; }

    /// <summary>The tenant, for objects belonging to one. Absent at platform and person scope.</summary>
    public long? TenantId { get; }

    /// <summary>
    /// The person, for objects belonging to a human rather than to any one tenant relationship.
    /// </summary>
    /// <remarks>
    /// Our own identifier for the person, seeded from their identity-provider user but not borrowed
    /// from it: a reference written today outlives the auth provider it came from, and one pointing
    /// at a vendor's identifier becomes an orphan the moment that vendor is gone.
    /// </remarks>
    public Guid? PersonId { get; }

    /// <summary>The store the reference is about, for objects that exist per seller per tenant.</summary>
    public long? StoreId { get; }

    /// <summary>The kind of object the reference points at.</summary>
    public AdyenReferenceType Type { get; }

    /// <summary>A reference for an object belonging to the platform itself, such as the liable balance account.</summary>
    public static AdyenReference ForPlatform(AdyenReferenceEnvironment environment, AdyenReferenceType type)
        => new(environment, AdyenReferenceScope.Platform, null, null, null, type);

    /// <summary>A reference for an object belonging to a tenant, such as its own legal entity or merchant account.</summary>
    public static AdyenReference ForTenant(AdyenReferenceEnvironment environment, long tenantId, AdyenReferenceType type)
        => new(environment, AdyenReferenceScope.Tenant, Positive(tenantId, nameof(tenantId)), null, null, type);

    /// <summary>
    /// A reference for an object belonging to a person rather than to any one tenant relationship — a
    /// seller's legal entity or account holder.
    /// </summary>
    /// <remarks>
    /// This is the scope that lets one verified person sell for several tenants without repeating
    /// KYC. It requires a genuine person identifier: anchoring it to whichever store came first works
    /// only until that seller leaves that tenant.
    /// </remarks>
    public static AdyenReference ForPerson(
        AdyenReferenceEnvironment environment,
        Guid personId,
        AdyenReferenceType type)
        => personId == Guid.Empty
            ? throw new ArgumentOutOfRangeException(
                nameof(personId),
                personId,
                "A person-scoped reference needs a person.")
            : new(environment, AdyenReferenceScope.Person, null, personId, null, type);

    /// <summary>
    /// A reference for an object that exists once per seller per tenant — a balance account, or the
    /// store itself.
    /// </summary>
    public static AdyenReference ForStore(
        AdyenReferenceEnvironment environment,
        long tenantId,
        long storeId,
        AdyenReferenceType type = AdyenReferenceType.Store)
        => new(
            environment,
            AdyenReferenceScope.Tenant,
            Positive(tenantId, nameof(tenantId)),
            null,
            Positive(storeId, nameof(storeId)),
            type);

    /// <summary>
    /// Reads a reference, accepting any digit width, either separator and either case of hexadecimal.
    /// </summary>
    /// <remarks>
    /// A store subject of zero is read as <b>absent</b>, not as store zero: <c>Dt000004_s0000000</c>
    /// is how a tenant-level record is spelled in references written before the scope segment
    /// existed, and it means the same thing as omitting the subject entirely.
    /// </remarks>
    /// <param name="value">The reference text.</param>
    /// <param name="reference">The parsed reference, when the text is well formed.</param>
    /// <returns><c>true</c> when the text parsed.</returns>
    public static bool TryParse(string? value, out AdyenReference reference)
    {
        reference = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = Pattern.Match(value);
        if (!match.Success)
        {
            return false;
        }

        var environment = match.Groups[1].Value switch
        {
            "D" => AdyenReferenceEnvironment.Dev,
            "Q" => AdyenReferenceEnvironment.Qa,
            _ => AdyenReferenceEnvironment.Prod,
        };

        var scope = AdyenReferenceScope.Platform;
        long? tenantId = null;
        Guid? personId = null;

        if (match.Groups[2].Success)
        {
            scope = AdyenReferenceScope.Tenant;
            if (!TryParseId(match.Groups[2].Value, out var parsedTenantId))
            {
                return false;
            }

            tenantId = parsedTenantId;
        }
        else if (match.Groups[3].Success)
        {
            scope = AdyenReferenceScope.Person;
            if (!Guid.TryParseExact(match.Groups[3].Value.ToLowerInvariant(), "N", out var parsedPersonId))
            {
                return false;
            }

            personId = parsedPersonId;
        }

        long? storeId = null;
        if (match.Groups[4].Success)
        {
            if (!TryParseId(match.Groups[4].Value, out var parsedStoreId))
            {
                return false;
            }

            // Zero is the legacy spelling of "no store" rather than a store in its own right.
            storeId = parsedStoreId == 0 ? null : parsedStoreId;
        }

        var type = match.Groups[5].Success
            ? FromCode(match.Groups[5].Value)
            : AdyenReferenceType.Store;

        reference = new AdyenReference(environment, scope, tenantId, personId, storeId, type);
        return true;
    }

    /// <summary>
    /// Writes the reference in its canonical form: underscore-separated, zero-padded, lowercase
    /// hexadecimal, and always carrying its type.
    /// </summary>
    /// <remarks>
    /// One spelling for everything we create, whatever width or separator the text was read at. The
    /// type segment is always emitted — including <c>_st</c> for a store, which references written
    /// before types existed leave off — so that every reference we write from here on says what it
    /// points at.
    /// </remarks>
    public override string ToString()
    {
        var builder = new StringBuilder(48);

        builder.Append(Environment switch
        {
            AdyenReferenceEnvironment.Dev => 'D',
            AdyenReferenceEnvironment.Qa => 'Q',
            _ => 'P',
        });

        switch (Scope)
        {
            case AdyenReferenceScope.Tenant when TenantId.HasValue:
                builder.Append('t');
                builder.Append(TenantId.Value.ToString(new string('0', TenantDigits), CultureInfo.InvariantCulture));
                break;

            case AdyenReferenceScope.Person when PersonId.HasValue:
                // "N": 32 lowercase hexadecimal digits, no hyphens — the only spelling the agreed
                // format accepts, and short enough to leave room under the 150-character cap.
                builder.Append('p');
                builder.Append(PersonId.Value.ToString("N", CultureInfo.InvariantCulture));
                break;

            default:
                break;
        }

        if (StoreId.HasValue)
        {
            builder.Append(Separator);
            builder.Append('s');
            builder.Append(StoreId.Value.ToString(new string('0', StoreDigits), CultureInfo.InvariantCulture));
        }

        builder.Append(Separator);
        builder.Append(ToCode(Type));

        return builder.ToString();
    }

    /// <inheritdoc />
    public bool Equals(AdyenReference other)
        => Environment == other.Environment
           && Scope == other.Scope
           && TenantId == other.TenantId
           && PersonId == other.PersonId
           && StoreId == other.StoreId
           && Type == other.Type;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is AdyenReference other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Environment, Scope, TenantId, PersonId, StoreId, Type);

    public static bool operator ==(AdyenReference left, AdyenReference right) => left.Equals(right);

    public static bool operator !=(AdyenReference left, AdyenReference right) => !left.Equals(right);

    private static string ToCode(AdyenReferenceType type) => type switch
    {
        AdyenReferenceType.Store => "st",
        AdyenReferenceType.LegalEntity => "le",
        AdyenReferenceType.AccountHolder => "ah",
        AdyenReferenceType.BalanceAccount => "ba",
        AdyenReferenceType.TransferInstrument => "ti",
        AdyenReferenceType.SplitConfiguration => "sc",
        AdyenReferenceType.MerchantAccount => "ma",
        AdyenReferenceType.Sweep => "sw",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown Adyen reference type."),
    };

    private static AdyenReferenceType FromCode(string code) => code switch
    {
        "st" => AdyenReferenceType.Store,
        "le" => AdyenReferenceType.LegalEntity,
        "ah" => AdyenReferenceType.AccountHolder,
        "ba" => AdyenReferenceType.BalanceAccount,
        "ti" => AdyenReferenceType.TransferInstrument,
        "sc" => AdyenReferenceType.SplitConfiguration,
        "ma" => AdyenReferenceType.MerchantAccount,
        _ => AdyenReferenceType.Sweep,
    };

    // Guards the identifiers rather than the text: a reference built from a zero or negative id would
    // be well formed and point at nothing.
    private static long Positive(long value, string parameterName)
        => value > 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, value, "Adyen reference identifiers are positive.");

    private static bool TryParseId(string digits, out long value)
        => long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
