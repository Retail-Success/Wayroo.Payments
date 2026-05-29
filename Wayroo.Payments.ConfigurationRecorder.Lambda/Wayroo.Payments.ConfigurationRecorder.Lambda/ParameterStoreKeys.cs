using System.Reflection;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda;

/// <summary>
/// Configuration keys this lambda expects to be supplied via AWS Systems Manager Parameter Store.
/// <see cref="Function"/> mounts SSM at <c>/luci/services/utility/OrdersClientOptions/</c>; the SSM
/// provider strips that path prefix and converts <c>/</c> → <c>:</c>, so the parameter
/// <c>/luci/services/utility/OrdersClientOptions/ApiBaseUrl</c> resolves to the config key
/// <c>ApiBaseUrl</c>.
/// </summary>
/// <remarks>
/// Each key declared here is validated on startup by <see cref="Function"/> alongside
/// <see cref="EnvironmentVariableKeys.Keys"/>.
/// </remarks>
public static class ParameterStoreKeys
{
    /// <summary>
    /// Orders API base URL — the recorder lambda calls it to resolve store/tenant from a ProPay account
    /// number. Backed by SSM parameter <c>/luci/services/utility/OrdersClientOptions/ApiBaseUrl</c>.
    /// </summary>
    public static readonly string OrdersApiBaseUrl = "ApiBaseUrl";

    /// <summary>
    /// Returns the configuration keys for SSM-backed settings. Pass
    /// <paramref name="parameterStoreFormat"/> = <c>true</c> to get them as SSM path segments
    /// (<c>:</c> → <c>/</c>) instead of the colon-separated config keys.
    /// </summary>
    public static IEnumerable<string> Keys(bool parameterStoreFormat = false)
    {
        var fields = typeof(ParameterStoreKeys).GetFields(
            BindingFlags.Public | BindingFlags.Static);

        return fields
            .Where(field => field.FieldType == typeof(string))
            .Select(field => field.GetValue(null) as string)
            .Where(value => value != null)
            .Select(value => parameterStoreFormat ? value!.Replace(":", "/") : value)
            .ToArray()!;
    }
}
