using Microsoft.AspNetCore.Mvc;

namespace Wayroo.Payments.API;

internal static class Routes
{
    public const string MinimumSupportedVersionString = "1.0";

    private static readonly string[] SupportedVersionStrings =
        [
            // The minimum version should be the version that all existing
            // callers are using and the version for enabling backwards compatibility.
            MinimumSupportedVersionString,

            // A version increase should only occur for a breaking change
            // and not replace the previous version until all callers have
            // been updated to support the breaking change.
            // "2.0",
        ];

    public static readonly ApiVersion[] SupportedVersions = [.. SupportedVersionStrings
        .Select(version => new ApiVersion(int.Parse(version.Split('.')[0]), int.Parse(version.Split('.')[1])))
        .OrderBy(version => version.MajorVersion)
        .ThenBy(version => version.MinorVersion)];

    private const string BaseRoute = "/api/payments/v{version:apiVersion}";

    public const string ConfigurationsRoute = $"{BaseRoute}/stores/{{storeId}}/configurations";

    // Tenant-scoped, unlike the configuration routes: every provider call selects its credentials
    // by tenant, and a store this service has no record of yet cannot tell us which tenant it
    // belongs to. Matches the shape of the Orders route being replaced.
    //
    // No provider segment: which provider a store transacts through is this service's job to know,
    // not the caller's, so that a store moving between providers touches no caller. Callers that
    // genuinely need to reach a specific provider pass it as an optional query parameter.
    public const string AccountsRoute = $"{BaseRoute}/tenants/{{tenantId}}/stores/{{storeId}}/account";

    /// <summary>
    /// The liveness probe. <b>Deployed contract:</b> the ECS container health check in
    /// <c>Wayroo.Payments.Infrastructure/Resources/PaymentsAPI.cs</c> curls this literal path, so
    /// renaming it means renaming it there too and redeploying both.
    /// </summary>
    public const string StatusRoute = "/status";

    /// <summary>
    /// Not mapped by this service, which answers probes on <see cref="StatusRoute"/>. Named only so
    /// <see cref="IsProbePath"/> covers it too, because <c>/health</c> is the conventional path
    /// across the estate and a probe pointed at it — by a load balancer, a monitor, or whoever maps
    /// it here next — should not become the loudest thing in the log group.
    /// </summary>
    public const string HealthRoute = "/health";

    /// <summary>
    /// Whether <paramref name="path"/> is a probe endpoint whose request log is noise.
    /// </summary>
    /// <remarks>
    /// Matched with <see cref="PathString.StartsWithSegments(PathString)"/> rather than equality so a
    /// trailing slash or a health-check sub-path (<c>/status/ready</c>) is covered too. Culture is
    /// irrelevant here — <c>PathString</c> compares ordinally, case-insensitively.
    /// </remarks>
    public static bool IsProbePath(PathString path) =>
        path.StartsWithSegments(StatusRoute) || path.StartsWithSegments(HealthRoute);
}
