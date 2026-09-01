namespace Wayroo.Payments.Models;

/// <summary>
/// Optional detail for an account refresh.
/// </summary>
/// <remarks>
/// Exists so a backfill can seed a store this service holds no record for. Payments does not call
/// other Wayroo services to discover an account reference — whoever drives the backfill already knows
/// it, so it supplies it here and the refresh records it alongside the account information.
/// </remarks>
public class RefreshPaymentAccountRequest
{
    /// <summary>
    /// The provider's reference for the store's merchant account — for ProPay, the account number.
    /// Omit it for a store already recorded here, in which case the recorded reference is used.
    /// </summary>
    public string? ProviderAccountRef { get; set; }
}
