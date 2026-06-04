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
}
