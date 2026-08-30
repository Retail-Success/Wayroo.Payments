using AwesomeAssertions;
using System.Reflection;
using EventStates = Wayroo.Payments.Messages.MigrationStates;
using ModelStates = Wayroo.Payments.Models.MigrationStates;

namespace Wayroo.Payments.API.UnitTests.Models;

/// <summary>
/// Holds the two <c>MigrationStates</c> constant sets to the same values.
/// </summary>
/// <remarks>
/// They are separate types on purpose — the event contract needs them for consumers who take only
/// <c>Wayroo.Payments.Messages</c>, and the model needs them for persistence, while
/// <c>Wayroo.Payments.Models</c> stays free of package references. The cost is that they can drift,
/// and drift would be silent and expensive: this service would persist one spelling and publish
/// another, so a consumer's state machine would never match the state it was sent. This test is what
/// makes that loud instead.
/// </remarks>
public class MigrationStateParityTests
{
    private static Dictionary<string, string> ConstantsOf(Type type) => type
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
        .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!);

    [Fact]
    public void TheTwoMigrationStateSets_DeclareTheSameNames()
    {
        ConstantsOf(typeof(ModelStates)).Keys
            .Should().BeEquivalentTo(ConstantsOf(typeof(EventStates)).Keys);
    }

    [Fact]
    public void TheTwoMigrationStateSets_AgreeOnEveryValue()
    {
        var eventStates = ConstantsOf(typeof(EventStates));

        foreach (var (name, value) in ConstantsOf(typeof(ModelStates)))
        {
            eventStates.Should().ContainKey(name);
            eventStates[name].Should().Be(value, $"'{name}' must be spelled identically on both sides");
        }
    }

    [Fact]
    public void TheStartingState_IsTheOneStoresAreSeededWith()
    {
        // The recorder seeds new stores here, and it is what makes the deploy inert.
        ModelStates.PropayActive.Should().Be("PropayActive");
        EventStates.PropayActive.Should().Be(ModelStates.PropayActive);
    }
}
