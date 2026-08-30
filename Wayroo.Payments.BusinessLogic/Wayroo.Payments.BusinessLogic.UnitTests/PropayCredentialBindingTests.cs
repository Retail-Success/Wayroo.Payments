using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RetailSuccess.PaymentGateway.Propay.Abstractions;
using Wayroo.Payments.BusinessLogic.Extensions;

namespace Wayroo.Payments.BusinessLogic.UnitTests;

/// <summary>
/// Proves the ProPay credentials actually bind from the shape Parameter Store delivers.
/// </summary>
/// <remarks>
/// <para>
/// The only test in the suite that exercises real configuration binding rather than a mocked
/// <c>IPropayClient</c>, and it exists because that gap hid a live defect: the credentials were bound
/// from a section named <c>Propay</c>, taken from the gateway package's own integration-test fixture.
/// Nothing in production puts them there.
/// </para>
/// <para>
/// <c>AddSystemsManager</c> is scoped to the vendor path, so a parameter at
/// <c>/luci/{env}/vendors/propay/tenants/3/termid</c> arrives as the root key <c>tenants:3:termid</c> —
/// under no section at all. Binding a section found nothing, startup still succeeded because
/// credentials are validated lazily per tenant, and the failure would first have appeared as a 500 on
/// a merchant's first balance call.
/// </para>
/// </remarks>
public class PropayCredentialBindingTests
{
    private const string TenantId = "3";

    /// <summary>
    /// Configuration in exactly the shape the SSM provider produces for the vendor path: credentials
    /// flattened to the root, base URIs nested one level down because that is how those parameters are
    /// laid out.
    /// </summary>
    private static IConfiguration FlattenedParameterStoreShape() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"tenants:{TenantId}:termid"] = "term-id",
            [$"tenants:{TenantId}:certstr"] = "cert-str",
            [$"tenants:{TenantId}:x509certificate"] = "x509",
            [$"tenants:{TenantId}:authenticationtoken"] = "auth-token",
            [$"tenants:{TenantId}:billeraccountid"] = "biller-account",
            [$"tenants:{TenantId}:sourceaccountcertstr"] = "source-cert",
            [$"tenants:{TenantId}:sourceaccounttermid"] = "source-term",
            ["PropayApiBaseUrisOptions:PropayRest"] = "https://xmltestapi.propay.com",
            ["PropayApiBaseUrisOptions:PropayXml"] = "https://xmltest.propay.com/API/ProPayAPI.aspx",
            ["PropayApiBaseUrisOptions:ProtectPayRest"] = "https://xmltestapi.propay.com",
        })
        .Build();

    private static ServiceProvider Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPaymentsBusinessLogic(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void TenantCredentials_BindFromTheFlattenedParameterStoreShape()
    {
        using var provider = Build(FlattenedParameterStoreShape());

        var credentials = provider.GetRequiredService<IPropayCredentialProvider>();

        credentials.TryGetPropayCredentials(long.Parse(TenantId), out var resolved).Should().BeTrue();
        resolved!.TermId.Should().Be("term-id");
        resolved.CertStr.Should().Be("cert-str");
        resolved.X509Certificate.Should().Be("x509");
        resolved.AuthenticationToken.Should().Be("auth-token");
        resolved.BillerAccountId.Should().Be("biller-account");
    }

    [Fact]
    public void ATenantWithNoParameters_DoesNotResolve()
    {
        using var provider = Build(FlattenedParameterStoreShape());

        var credentials = provider.GetRequiredService<IPropayCredentialProvider>();

        // Lazily per tenant: a tenant nobody has provisioned fails only when a call is made for it,
        // which is why a missing credential cannot be caught at startup.
        credentials.TryGetPropayCredentials(999, out _).Should().BeFalse();
    }

    [Fact]
    public void TheGatewayResolves_WithTheBaseUrisBoundFromTheirSection()
    {
        using var provider = Build(FlattenedParameterStoreShape());

        // Resolving IPropayClient constructs a typed HttpClient per API, each of which throws on a
        // blank base address — so this fails if the base-URI section binding regresses.
        var act = () => provider.GetRequiredService<IPropayClient>();

        act.Should().NotThrow();
    }

    /// <summary>
    /// The second configuration layer: a tenant may carry its own base URL in Parameter Store, and it
    /// wins over the environment-wide default for that tenant alone. Some tenants have these set in
    /// some environments, so it is a live path, not a hypothetical.
    /// </summary>
    [Fact]
    public void APerTenantBaseUrlOverride_BindsAlongsideTheCredentials()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"tenants:{TenantId}:termid"] = "term-id",
                [$"tenants:{TenantId}:certstr"] = "cert-str",
                [$"tenants:{TenantId}:x509certificate"] = "x509",
                [$"tenants:{TenantId}:authenticationtoken"] = "auth-token",
                [$"tenants:{TenantId}:billeraccountid"] = "biller-account",
                [$"tenants:{TenantId}:sourceaccountcertstr"] = "source-cert",
                [$"tenants:{TenantId}:sourceaccounttermid"] = "source-term",
                // Lowercase, exactly as the parameter is named under the vendor path.
                [$"tenants:{TenantId}:propayrestbaseurl"] = "https://tenant-specific.propay.example",
                ["PropayApiBaseUrisOptions:PropayRest"] = "https://xmltestapi.propay.com",
                ["PropayApiBaseUrisOptions:PropayXml"] = "https://xmltest.propay.com/API/ProPayAPI.aspx",
                ["PropayApiBaseUrisOptions:ProtectPayRest"] = "https://xmltestapi.propay.com",
            })
            .Build();

        using var provider = Build(configuration);

        provider.GetRequiredService<IPropayCredentialProvider>()
            .TryGetPropayCredentials(long.Parse(TenantId), out var resolved).Should().BeTrue();
        resolved!.PropayRestBaseUrl.Should().Be("https://tenant-specific.propay.example");
    }

    /// <summary>
    /// A tenant without an override leaves the field null, which is what makes the client fall back to
    /// the environment-wide default rather than sending a request at a blank host.
    /// </summary>
    [Fact]
    public void ATenantWithoutAnOverride_LeavesTheBaseUrlUnset()
    {
        using var provider = Build(FlattenedParameterStoreShape());

        provider.GetRequiredService<IPropayCredentialProvider>()
            .TryGetPropayCredentials(long.Parse(TenantId), out var resolved).Should().BeTrue();
        resolved!.PropayRestBaseUrl.Should().BeNullOrEmpty();
    }
}
