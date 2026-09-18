namespace Wayroo.Payments.Models;

/// <summary>
/// Which of <i>our</i> environments an Adyen object belongs to, carried as the first character of
/// its reference.
/// </summary>
/// <remarks>
/// <para>
/// We have three environments and Adyen has two. Dev and QA both point at Adyen's test environment,
/// sharing one balance platform and one pool of legal entities, so this letter is the only thing
/// keeping their records apart in a combined list.
/// </para>
/// <para>
/// Distinct from the Adyen client library's own <c>AdyenReferenceEnvironment</c>, which names Adyen's two
/// environments rather than our three — <see cref="Dev"/> and <see cref="Qa"/> both run against
/// Adyen's test environment.
/// </remarks>
public enum AdyenReferenceEnvironment
{
    /// <summary>Dev. Adyen test.</summary>
    Dev = 0,

    /// <summary>QA. Adyen test, alongside dev.</summary>
    Qa,

    /// <summary>Production. Adyen live.</summary>
    Prod,
}
