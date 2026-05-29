namespace Wayroo.Payments.DataAccess.IntegrationTests.Fixtures;

/// <summary>
/// Enables tests to re-use a single <see cref="TestFixture"/> (and its DynamoDB Local container)
/// across the collection.
/// </summary>
[CollectionDefinition(nameof(TestCollection))]
public class TestCollection : ICollectionFixture<TestFixture>
{
}
