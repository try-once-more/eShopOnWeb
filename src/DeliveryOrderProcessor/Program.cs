using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Application Insights isn't enabled by default. See https://aka.ms/AAt8mw4.
//builder.Services
//    .AddApplicationInsightsTelemetryWorkerService()
//    .ConfigureFunctionsApplicationInsights();

var cosmosDbConfig = builder.Configuration
    .GetRequiredSection(nameof(AzureCosmosDB))
    .Get<AzureCosmosDB>()
    ?? throw new InvalidOperationException($"Invalid '{nameof(AzureCosmosDB)}' config section");

var cosmosClient = new CosmosClient(cosmosDbConfig.ConnectionString, new CosmosClientOptions
{
    UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions()
    {
        PropertyNameCaseInsensitive = true
    }
});
var databaseResponse = await cosmosClient.CreateDatabaseIfNotExistsAsync(cosmosDbConfig.Database);
var containerResponse = await databaseResponse.Database.CreateContainerIfNotExistsAsync(
    new ContainerProperties
    {
        Id = cosmosDbConfig.Container,
        PartitionKeyPath = "/customerId",
        PartitionKeyDefinitionVersion = PartitionKeyDefinitionVersion.V2
    },
    ThroughputProperties.CreateManualThroughput(400)
);

builder.Services.AddSingleton(sp => containerResponse.Container);
builder.Services.AddLogging();

await builder.Build().RunAsync();

file class AzureCosmosDB
{
    public required string ConnectionString { get; init; }
    public required string Database { get; init; }
    public required string Container { get; init; }
}
