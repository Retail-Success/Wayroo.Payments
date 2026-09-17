using AwesomeAssertions;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.BusinessLogic.UnitTests;

/// <summary>
/// Pins the reference convention. These assertions are about persisted text: a reference is written
/// once at creation and read for the life of the account, so a change that makes one of these fail
/// invalidates references already out there rather than merely breaking a test.
/// </summary>
public class AdyenReferenceTests
{
    // The worked example from the agreed convention, so the spelling here is the spelling there.
    private static readonly Guid PersonId = Guid.ParseExact("8f14e45fceea167a5a36dedd4bea2543", "N");

    [Fact]
    public void ForStore_WritesTheStoreTypeExplicitly()
    {
        // References written before types existed carry no trailing segment; everything written from
        // here on says what it points at.
        AdyenReference.ForStore(AdyenReferenceEnvironment.Dev, 4, 31610)
            .ToString().Should().Be("Dt000004_s0031610_st");
    }

    [Theory]
    [InlineData(AdyenReferenceType.BalanceAccount, "Dt000004_s0031610_ba")]
    [InlineData(AdyenReferenceType.LegalEntity, "Dt000004_s0031610_le")]
    [InlineData(AdyenReferenceType.AccountHolder, "Dt000004_s0031610_ah")]
    [InlineData(AdyenReferenceType.Store, "Dt000004_s0031610_st")]
    public void ForStore_AppendsTheType(AdyenReferenceType type, string expected)
    {
        AdyenReference.ForStore(AdyenReferenceEnvironment.Dev, 4, 31610, type).ToString().Should().Be(expected);
    }

    [Fact]
    public void ForPerson_DropsTheTenantEntirely()
    {
        // Reusing one verified legal entity is what spares a seller from repeating KYC for every
        // tenant they join, so this scope carries no tenant at all.
        AdyenReference.ForPerson(AdyenReferenceEnvironment.Dev, PersonId, AdyenReferenceType.LegalEntity)
            .ToString().Should().Be("Dp8f14e45fceea167a5a36dedd4bea2543_le");
    }

    [Fact]
    public void ForPerson_WritesTheIdentifierAsLowercaseHexWithNoSeparators()
    {
        var reference = AdyenReference
            .ForPerson(AdyenReferenceEnvironment.Prod, Guid.NewGuid(), AdyenReferenceType.AccountHolder)
            .ToString();

        reference.Should().MatchRegex("^Pp[0-9a-f]{32}_ah$");
    }

    [Fact]
    public void ForPerson_RefusesAnAbsentPerson()
    {
        // An empty identifier would produce a reference every person-scoped object shared.
        var build = () => AdyenReference.ForPerson(
            AdyenReferenceEnvironment.Dev,
            Guid.Empty,
            AdyenReferenceType.LegalEntity);

        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(AdyenReferenceType.LegalEntity, "Dt000004_le")]
    [InlineData(AdyenReferenceType.AccountHolder, "Dt000004_ah")]
    [InlineData(AdyenReferenceType.MerchantAccount, "Dt000004_ma")]
    [InlineData(AdyenReferenceType.SplitConfiguration, "Dt000004_sc")]
    public void ForTenant_CarriesNoStoreSubject(AdyenReferenceType type, string expected)
    {
        AdyenReference.ForTenant(AdyenReferenceEnvironment.Dev, 4, type).ToString().Should().Be(expected);
    }

    [Fact]
    public void ForPlatform_CarriesNeitherScopeNorSubject()
    {
        AdyenReference.ForPlatform(AdyenReferenceEnvironment.Dev, AdyenReferenceType.BalanceAccount)
            .ToString().Should().Be("D_ba");
    }

    [Theory]
    [InlineData(AdyenReferenceEnvironment.Dev, "Dt000004_s0031610_st")]
    [InlineData(AdyenReferenceEnvironment.Qa, "Qt000004_s0031610_st")]
    [InlineData(AdyenReferenceEnvironment.Prod, "Pt000004_s0031610_st")]
    public void TheEnvironmentLetterLeadsEveryReference(AdyenReferenceEnvironment environment, string expected)
    {
        // Dev and QA share one Adyen test environment, so this letter is the only separator between them.
        AdyenReference.ForStore(environment, 4, 31610).ToString().Should().Be(expected);
    }

    /// <summary>
    /// The separator is an underscore because it is the only character every Adyen reference field
    /// accepts. A colon is fine on a legal entity, an account holder and a balance account, and is
    /// rejected outright on a store and a merchant account — where the reference becomes the object's
    /// own identifier, so a rejected one cannot be worked around.
    /// </summary>
    [Fact]
    public void EveryReferenceWeWrite_UsesTheOnlySeparatorEveryAdyenFieldAccepts()
    {
        string[] written =
        [
            AdyenReference.ForStore(AdyenReferenceEnvironment.Dev, 4, 31610).ToString(),
            AdyenReference.ForStore(AdyenReferenceEnvironment.Dev, 4, 31610, AdyenReferenceType.BalanceAccount).ToString(),
            AdyenReference.ForPerson(AdyenReferenceEnvironment.Dev, PersonId, AdyenReferenceType.LegalEntity).ToString(),
            AdyenReference.ForTenant(AdyenReferenceEnvironment.Dev, 4, AdyenReferenceType.MerchantAccount).ToString(),
            AdyenReference.ForPlatform(AdyenReferenceEnvironment.Dev, AdyenReferenceType.BalanceAccount).ToString(),
        ];

        written.Should().AllSatisfy(reference => reference.Should().NotContain(":"));
        written.Should().AllSatisfy(reference => reference.Should().MatchRegex("^[DQP][a-z0-9_]*$"));
    }

    [Theory]
    [InlineData("Dt000004_s0031244")]
    [InlineData("Dt0004_s31244")]
    [InlineData("Dt4_s31244")]
    [InlineData("Dt000004:s0031244")]
    public void TryParse_AcceptsEveryDigitWidthAndEitherSeparator(string value)
    {
        // Both digit widths are live — Management.UI carries a comment about exactly this mismatch —
        // and the platform's own store identifiers keep their colons, so both have to read.
        AdyenReference.TryParse(value, out var reference).Should().BeTrue();

        reference.TenantId.Should().Be(4);
        reference.StoreId.Should().Be(31244);
    }

    [Fact]
    public void ReferencesAreComparedByValueSoSpellingCannotSplitOneObjectInTwo()
    {
        AdyenReference.TryParse("Dt0004:s31244", out var legacy).Should().BeTrue();
        AdyenReference.TryParse("Dt000004_s0031244_st", out var canonical).Should().BeTrue();

        legacy.Should().Be(canonical);
        legacy.GetHashCode().Should().Be(canonical.GetHashCode());
    }

    /// <summary>
    /// The transformation the convention asks for: the platform's own store identifiers keep their
    /// colons, and only the value sent to Adyen changes.
    /// </summary>
    [Fact]
    public void ToString_TransformsAPlatformStoreIdentifierIntoTheValueAdyenAccepts()
    {
        AdyenReference.TryParse("Dt000004:s0031610", out var internalIdentifier).Should().BeTrue();

        internalIdentifier.ToString().Should().Be("Dt000004_s0031610_st");
    }

    [Fact]
    public void ToString_NormalisesToThePaddedFormWhateverWidthItWasReadAt()
    {
        AdyenReference.TryParse("Dt4_s31244", out var reference).Should().BeTrue();

        reference.ToString().Should().Be("Dt000004_s0031244_st");
    }

    [Fact]
    public void TryParse_ReadsAZeroStoreSubjectAsNoStore()
    {
        // The spelling a tenant-level record carries in references written before the scope segment
        // existed; the authorization token validator reads it the same way.
        AdyenReference.TryParse("Dt000004:s0000000", out var reference).Should().BeTrue();

        reference.StoreId.Should().BeNull();
        reference.TenantId.Should().Be(4);
        reference.Scope.Should().Be(AdyenReferenceScope.Tenant);
    }

    [Theory]
    [InlineData("Dt000004_s0031610", AdyenReferenceType.Store)]
    [InlineData("Dt000004_s0031610_st", AdyenReferenceType.Store)]
    [InlineData("Dt000004_s0031610_ba", AdyenReferenceType.BalanceAccount)]
    [InlineData("Dp8f14e45fceea167a5a36dedd4bea2543_le", AdyenReferenceType.LegalEntity)]
    [InlineData("Dp8f14e45fceea167a5a36dedd4bea2543_ah", AdyenReferenceType.AccountHolder)]
    [InlineData("Dp8f14e45fceea167a5a36dedd4bea2543_ti", AdyenReferenceType.TransferInstrument)]
    [InlineData("D_ba", AdyenReferenceType.BalanceAccount)]
    [InlineData("Dt000004_sw", AdyenReferenceType.Sweep)]
    public void TryParse_TreatsAMissingTrailingSegmentAsAStore(string value, AdyenReferenceType expected)
    {
        AdyenReference.TryParse(value, out var reference).Should().BeTrue();

        reference.Type.Should().Be(expected);
    }

    [Fact]
    public void TryParse_ReadsAPersonIdentifierWhicheverCaseItIsWrittenIn()
    {
        AdyenReference
            .TryParse("Dp8F14E45FCEEA167A5A36DEDD4BEA2543_le", out var reference)
            .Should().BeTrue();

        reference.Scope.Should().Be(AdyenReferenceScope.Person);
        reference.PersonId.Should().Be(PersonId);
        reference.TenantId.Should().BeNull();
        reference.StoreId.Should().BeNull();
        reference.ToString().Should().Be("Dp8f14e45fceea167a5a36dedd4bea2543_le");
    }

    [Theory]
    [InlineData("Dt000004:s0031167")]
    [InlineData("Qt000004:s0028938")]
    [InlineData("Dt000004:s0000000")]
    [InlineData("Dt000005:s031135")]
    public void TryParse_ReadsTheReferencesAlreadyInUseAsFronteggTenantIdentifiers(string value)
    {
        // Sampled from configuration, tests and captured traffic. These keep their colons inside the
        // platform, so the convention has to keep reading them unchanged.
        AdyenReference.TryParse(value, out var reference).Should().BeTrue();

        reference.Type.Should().Be(AdyenReferenceType.Store);
        reference.Scope.Should().Be(AdyenReferenceScope.Tenant);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Xt000004_s0031610")]       // only D, Q and P are environments
    [InlineData("Dt000004_s0031610_zz")]    // not a type code
    [InlineData("Dt000004_s0031610_bl")]    // business lines carry no reference, so there is no code
    [InlineData("Dx000004_s0031610")]       // not a scope key
    [InlineData("t000004_s0031610")]        // no environment
    [InlineData("Dt000004_0031610")]        // subject is missing its s
    [InlineData("Dt00A004_s0031610")]       // tenant identifiers are digits
    [InlineData("Dp0031610_le")]            // a person is a 32-character identifier, not a store id
    [InlineData("Dp8f14e45fceea167a5a36dedd4bea25_le")]   // 31 characters
    [InlineData("Dp8f14e45fceea167a5a36dedd4bea2543a_le")] // 33 characters
    [InlineData(" Dt000004_s0031610 ")]     // a reference is the whole value, untrimmed
    public void TryParse_RejectsAnythingOutsideTheConvention(string? value)
    {
        AdyenReference.TryParse(value, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BuildingAReferenceForANonPositiveIdentifierThrows(long id)
    {
        // A reference built from zero would be indistinguishable from the legacy "no store" spelling.
        var build = () => AdyenReference.ForStore(AdyenReferenceEnvironment.Dev, 4, id);

        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EveryReferenceWeWriteFitsTheTightestAdyenLimit()
    {
        // Sweep references are capped at 80 characters, tighter than the 150 stores and balance
        // accounts allow. Long ids are used here to show the headroom is structural, not incidental.
        var longestNumeric = AdyenReference.ForStore(
            AdyenReferenceEnvironment.Prod,
            long.MaxValue,
            long.MaxValue,
            AdyenReferenceType.Sweep);

        var longestPerson = AdyenReference.ForPerson(
            AdyenReferenceEnvironment.Prod,
            Guid.NewGuid(),
            AdyenReferenceType.AccountHolder);

        longestNumeric.ToString().Length.Should().BeLessThan(80);
        longestPerson.ToString().Length.Should().BeLessThan(80);
    }

    [Theory]
    [InlineData("Dt000004_s0031610_st")]
    [InlineData("Dt000004_s0031610_ba")]
    [InlineData("Dp8f14e45fceea167a5a36dedd4bea2543_le")]
    [InlineData("Dt000004_ma")]
    [InlineData("D_ba")]
    public void ParsingACanonicalReferenceAndWritingItBackIsLossless(string value)
    {
        AdyenReference.TryParse(value, out var reference).Should().BeTrue();

        reference.ToString().Should().Be(value);
    }
}
