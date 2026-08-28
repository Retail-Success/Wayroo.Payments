using Wayroo.Common.Models.Events;

namespace Wayroo.Payments.Eventing.UnitTests;

/// <summary>
/// Stand-in for the real contracts (which land with D4 in Wayroo.Payments.Messages). Only exists so
/// the publisher can be exercised against something that implements the envelope's type identity.
/// </summary>
internal sealed record TestStoreEvent : IStoreScopedEvent
{
    public static string Source => "rs.payments";
    public static string EventName => "TestStoreThingHappened";
    public static int Version => 1;

    public required string ProviderId { get; init; }
}
