using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Docker.DotNet;
using Docker.DotNet.Models;
using WireMock.Server;

namespace Wayroo.Payments.ConfigurationRecorder.Lambda.IntegrationTests.Fixtures;

/// <summary>
/// Stands up the real-but-local dependencies the recorder lambda needs to be exercised end-to-end:
/// <list type="bullet">
///   <item>An <c>amazon/dynamodb-local</c> container (via Docker) so the lambda's repository writes
///   land in a real DynamoDB and can be read back for verification.</item>
///   <item>A WireMock.Net HTTP server that stubs the Orders API's <c>/propay/accounts/{n}/store</c>
///   endpoint, so the recorder's Refit client makes a real HTTP call and gets a known response.</item>
/// </list>
/// Both are started lazily on first use, so tests that don't need them (e.g. the init test) don't pay
/// the startup cost.
/// </summary>
public class TestFixture : IDisposable
{
    private static readonly DockerClient _dockerClient;
    private string? _createdContainer;
    private int? _createdContainerPort;
    private WireMockServer? _ordersStub;

    /// <summary>HTTP base URL of the in-process WireMock stub for the Orders API.</summary>
    public string OrdersApiBaseUrl => GetOrCreateOrdersStub().Url
        ?? throw new InvalidOperationException("WireMock server did not expose a URL.");

    /// <summary>HTTP base URL of the local DynamoDB instance.</summary>
    public string DynamoServiceUrl => _createdContainerPort is null
        ? throw new InvalidOperationException("DynamoDB Local container has not been started.")
        : $"http://localhost:{_createdContainerPort}";

    static TestFixture()
    {
        // As this is intended to be local, use the default Docker configuration.
        _dockerClient = new DockerClientConfiguration().CreateClient();
    }

    public WireMockServer GetOrCreateOrdersStub()
    {
        _ordersStub ??= WireMockServer.Start();
        return _ordersStub;
    }

    public async Task<IAmazonDynamoDB> StartDynamoDbLocalAsync()
    {
        if (_createdContainerPort is not null)
        {
            return CreateDynamoDbClient();
        }

        await EnsureImageExistsAsync("amazon/dynamodb-local:latest");

        var existingContainers = await _dockerClient.Containers.ListContainersAsync(
            new ContainersListParameters { All = true });
        var existingPorts = existingContainers
            .SelectMany(container => container.Ports.Select(port => (int)port.PublicPort))
            .ToList();

        var containerPort = 8000;
        while (existingPorts.Contains(containerPort))
        {
            containerPort++;
        }
        _createdContainerPort = containerPort;

        var container = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Cmd = ["-jar", "DynamoDBLocal.jar", "-inMemory", "-sharedDb", "-port", $"{containerPort}"],
            Image = "amazon/dynamodb-local:latest",
            Name = $"Payments-Recorder-IntegrationTests-{Guid.NewGuid()}",
            HostConfig = new HostConfig
            {
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    {
                        $"{containerPort}/tcp",
                        new List<PortBinding> { new() { HostPort = containerPort.ToString() } }
                    },
                },
            },
            ExposedPorts = new Dictionary<string, EmptyStruct> { { $"{containerPort}/tcp", default } },
            WorkingDir = "/home/dynamodblocal",
        });

        _createdContainer = container.ID;

        var started = await _dockerClient.Containers.StartContainerAsync(_createdContainer, new ContainerStartParameters());
        if (!started)
        {
            throw new Exception("Failed to start container to run local DynamoDB instance.");
        }

        return CreateDynamoDbClient();
    }

    /// <summary>
    /// Creates the payment-configuration table (numeric StoreId PK, ProviderId SK) on the local DynamoDB
    /// and returns its name. Each test should call this so it runs against an isolated table.
    /// </summary>
    public async Task<string> EstablishTableAsync(IAmazonDynamoDB client)
    {
        var tableName = $"PaymentConfiguration-{Guid.NewGuid()}";
        await client.CreateTableAsync(new CreateTableRequest
        {
            TableName = tableName,
            KeySchema =
            [
                new KeySchemaElement { AttributeName = "StoreId", KeyType = KeyType.HASH },
                new KeySchemaElement { AttributeName = "ProviderId", KeyType = KeyType.RANGE },
            ],
            AttributeDefinitions =
            [
                new AttributeDefinition { AttributeName = "StoreId", AttributeType = ScalarAttributeType.N },
                new AttributeDefinition { AttributeName = "ProviderId", AttributeType = ScalarAttributeType.S },
            ],
            BillingMode = BillingMode.PAY_PER_REQUEST,
        });
        return tableName;
    }

    private IAmazonDynamoDB CreateDynamoDbClient()
        // Credentials are required by the SDK but not checked by DynamoDB Local.
        => new AmazonDynamoDBClient(
            new BasicAWSCredentials("notUsed", "notUsed"),
            new AmazonDynamoDBConfig { ServiceURL = DynamoServiceUrl });

    private async Task EnsureImageExistsAsync(string imageName)
    {
        try
        {
            await _dockerClient.Images.InspectImageAsync(imageName);
        }
        catch (DockerImageNotFoundException)
        {
            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = imageName },
                new AuthConfig(),
                new Progress<JSONMessage>());
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try { _ordersStub?.Stop(); } catch (Exception ex) { Console.WriteLine(ex.Message); }
        try { _ordersStub?.Dispose(); } catch (Exception ex) { Console.WriteLine(ex.Message); }

        if (string.IsNullOrWhiteSpace(_createdContainer))
        {
            return;
        }

        // Kill + remove the DynamoDB Local container. Swallow failures — this is the end of the run.
        try
        {
            _dockerClient.Containers.KillContainerAsync(_createdContainer, new ContainerKillParameters()).Wait();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        try
        {
            _dockerClient.Containers.RemoveContainerAsync(_createdContainer, new ContainerRemoveParameters { Force = true }).Wait();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}
