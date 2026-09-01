using AwesomeAssertions;
using Wayroo.Payments.Messages;
using Wayroo.Payments.Models;

namespace Wayroo.Payments.API.UnitTests.Models;

/// <summary>
/// Holds <see cref="PaymentAccountStatus"/> (the API read model) and
/// <see cref="MerchantAccountStatus"/> (the event contract) to the same set of values.
/// </summary>
/// <remarks>
/// The two are separate types on purpose — <c>Wayroo.Payments.Models</c> carries no package
/// references, so the API contract stays clear of the event package and its additive-minor
/// versioning policy. The cost of that choice is that they can drift, and drift would be silent:
/// the day the refresh starts publishing <c>MerchantAccountStatusChanged</c>, a status the event
/// enum has never heard of becomes a poison message in every consumer. This test is the thing that
/// makes the drift loud instead.
/// </remarks>
public class PaymentAccountStatusParityTests
{
    [Fact]
    public void TheNeutralStatusEnums_DeclareTheSameMembers()
    {
        var apiStatuses = Enum.GetNames<PaymentAccountStatus>();
        var eventStatuses = Enum.GetNames<MerchantAccountStatus>();

        apiStatuses.Should().BeEquivalentTo(eventStatuses);
    }

    [Fact]
    public void TheNeutralStatusEnums_AgreeOnEveryValue()
    {
        // Names alone would let the two disagree numerically, which a cast between them would then
        // silently mistranslate.
        foreach (var name in Enum.GetNames<PaymentAccountStatus>())
        {
            var apiValue = (int)Enum.Parse<PaymentAccountStatus>(name);
            var eventValue = (int)Enum.Parse<MerchantAccountStatus>(name);

            apiValue.Should().Be(eventValue, $"'{name}' must mean the same thing on both contracts");
        }
    }
}
