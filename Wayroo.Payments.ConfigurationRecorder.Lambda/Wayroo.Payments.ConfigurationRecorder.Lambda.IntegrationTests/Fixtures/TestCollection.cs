namespace Wayroo.Payments.ConfigurationRecorder.Lambda.IntegrationTests.Fixtures;

/// <summary>
/// Shares a single <see cref="TestFixture"/> (DynamoDB Local container + WireMock Orders stub) across
/// all tests in the collection. Serializes the tests so the process-wide environment variables don't
/// race.
/// </summary>
[CollectionDefinition(nameof(TestCollection))]
public class TestCollection : ICollectionFixture<TestFixture> { }
