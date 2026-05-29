using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Wayroo.Payments.DataAccess.IntegrationTests.Fixtures;

/// <summary>
/// Hosts a local DynamoDB instance (amazon/dynamodb-local) in a Docker container for the duration of the
/// test collection and creates a fresh payment-configuration table per request. Mirrors the fixture in
/// Wayroo.Notification.DataAccess.ComponentTests / Wayroo.ContentLibrary.DataAccess.IntegrationTests.
/// </summary>
public class TestFixture : IDisposable
{
    private static readonly DockerClient _dockerClient;
    private string? _createdContainer;
    private int? _createdContainerPort;

    private static string GetNewTableName() => $"PaymentConfiguration-{Guid.NewGuid()}";

    static TestFixture()
    {
        // As this is intended to be local, use the default configuration.
        _dockerClient = new DockerClientConfiguration().CreateClient();
    }

    public async Task<IAmazonDynamoDB> GetTestClientAsync()
    {
        if (_createdContainerPort != null)
        {
            return GetClient();
        }

        await EnsureImageExistsAsync("amazon/dynamodb-local:latest");

        var existingContainers = await _dockerClient.Containers.ListContainersAsync(
            new ContainersListParameters { All = true });

        var existingPorts = existingContainers
            .SelectMany(container => container.Ports.Select(port => (int)port.PublicPort))
            .ToList();

        // Find the next available port, starting from the DynamoDB Local default.
        var containerPortForDynamoDbAccess = 8000;
        do
        {
            if (existingPorts.Contains(containerPortForDynamoDbAccess))
            {
                containerPortForDynamoDbAccess++;
                continue;
            }

            _createdContainerPort = containerPortForDynamoDbAccess;
        } while (_createdContainerPort == null);

        var dynamoDbContainer = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Cmd = ["-jar", "DynamoDBLocal.jar", "-inMemory", "-sharedDb", "-port", $"{containerPortForDynamoDbAccess}"],
            Image = "amazon/dynamodb-local:latest",
            Name = $"Payments-IntegrationTests-{Guid.NewGuid()}",
            HostConfig = new HostConfig
            {
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    {
                        $"{containerPortForDynamoDbAccess}/tcp",
                        new List<PortBinding> { new() { HostPort = containerPortForDynamoDbAccess.ToString() } }
                    },
                },
            },
            ExposedPorts = new Dictionary<string, EmptyStruct> { { $"{containerPortForDynamoDbAccess}/tcp", default } },
            WorkingDir = "/home/dynamodblocal",
        });

        _createdContainer = dynamoDbContainer.ID;

        var started = await _dockerClient.Containers.StartContainerAsync(_createdContainer, new ContainerStartParameters());
        if (!started)
        {
            throw new Exception("Failed to start container to run local DynamoDB instance.");
        }

        var containerState = await _dockerClient.Containers.InspectContainerAsync(_createdContainer);
        if (containerState?.State?.Status != "running")
        {
            throw new Exception("Failed to start container to run local DynamoDB instance.");
        }

        return GetClient();
    }

    private IAmazonDynamoDB GetClient()
        // NOTE: Credentials are required but aren't used against DynamoDB Local.
        => new AmazonDynamoDBClient(
            new BasicAWSCredentials(accessKey: "notUsed", secretKey: "notUsed"),
            new AmazonDynamoDBConfig { ServiceURL = $"http://localhost:{_createdContainerPort}" });

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

    /// <summary>
    /// Creates a fresh payment-configuration table (numeric StoreId PK, ProviderId SK) and returns
    /// options pointing at it, so each test runs against isolated data.
    /// </summary>
    public async Task<DynamoDbClientOptions> EstablishExistingTable(IAmazonDynamoDB client)
    {
        var options = new DynamoDbClientOptions { PaymentConfigurationTableName = GetNewTableName() };

        var request = new CreateTableRequest
        {
            TableName = options.PaymentConfigurationTableName,
            KeySchema =
            [
                new KeySchemaElement { AttributeName = PaymentConfigurationSchemaProvider.AttributeNameForPartitionKey, KeyType = KeyType.HASH },
                new KeySchemaElement { AttributeName = PaymentConfigurationSchemaProvider.AttributeNameForSortKey, KeyType = KeyType.RANGE },
            ],
            AttributeDefinitions =
            [
                new AttributeDefinition { AttributeName = PaymentConfigurationSchemaProvider.AttributeNameForPartitionKey, AttributeType = ScalarAttributeType.N },
                new AttributeDefinition { AttributeName = PaymentConfigurationSchemaProvider.AttributeNameForSortKey, AttributeType = ScalarAttributeType.S },
            ],
            BillingMode = BillingMode.PAY_PER_REQUEST,
        };

        await client.CreateTableAsync(request);
        return options;
    }

    public PaymentConfigurationRepository GetRepository(IAmazonDynamoDB client, DynamoDbClientOptions options)
        => new(client, options, NullLogger<PaymentConfigurationRepository>.Instance);

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (string.IsNullOrWhiteSpace(_createdContainer))
        {
            return;
        }

        // As this is intended to be local, kill and remove the container once done. Swallow failures —
        // this is the end of the run and there's no recovery action to take.
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
