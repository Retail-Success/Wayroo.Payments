namespace Wayroo.Payments.Models;

/// <summary>
/// Stable identifiers for the log events this service raises CloudWatch alarms on.
/// </summary>
/// <remarks>
/// <para>
/// Emitted as the <see cref="PropertyName"/> property of a log event and matched by a CloudWatch
/// metric filter in <c>Wayroo.Payments.Infrastructure/Resources/PaymentsAPI.cs</c>. The point of the
/// indirection is that the alarm does not depend on the wording of a log message: rephrase the
/// sentence and the metric filter keeps matching, because it matches on this value instead.
/// </para>
/// <para>
/// Lives in this project because it is the one both the tier that emits these
/// (<c>Wayroo.Payments.BusinessLogic</c>) and the CDK app that filters on them can see — the CDK does
/// not reference <c>Wayroo.Payments.API</c> (a web project), and inlining the strings there is the
/// hand-sync problem that file already carries for its environment variable keys.
/// </para>
/// <para>
/// <b>These values are a deployed contract.</b> Renaming one silently stops its alarm matching:
/// the metric filter finds nothing, the metric reports no data, and
/// <c>TreatMissingData.NOT_BREACHING</c> reads that as healthy. Change one only alongside the
/// construct, and expect to redeploy both.
/// </para>
/// </remarks>
public static class PaymentsLogSignals
{
    /// <summary>
    /// Log event property carrying the signal. Matched as <c>$.Properties.PaymentsSignal</c>, which is
    /// where Serilog's JSON formatter puts a message template property.
    /// </summary>
    public const string PropertyName = "PaymentsSignal";

    /// <summary>
    /// An account refresh completed without finding an account for the store: this service holds no
    /// provider account reference for it and the caller supplied none.
    /// </summary>
    /// <remarks>
    /// Not a failure on its own — a backfill sweeping every store expects to meet stores that never
    /// onboarded, and the endpoint answers <c>200</c> with <c>accountExists: false</c>. It is the
    /// <i>rate</i> that is worth alarming on: a caller that has stopped supplying
    /// <c>providerAccountRef</c>, or a backfill pointed at the wrong tenant, shows up here as a flood
    /// of stores that all suddenly have no account.
    /// </remarks>
    public const string RefreshAccountNoAccount = nameof(RefreshAccountNoAccount);

    /// <summary>
    /// A balance read found no recorded account standing for the store and went to the provider for it.
    /// </summary>
    /// <remarks>
    /// Expected once per store, before the backfill reaches it — the store then "heals" and later
    /// balance reads are a single provider call. So this should trend to zero and stay there. A
    /// sustained rate means stores are not healing, which points at the recording write failing rather
    /// than at the read.
    /// </remarks>
    public const string GetBalanceStatusBackfilled = nameof(GetBalanceStatusBackfilled);
}
