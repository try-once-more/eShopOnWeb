using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderItemsReserver;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Application Insights isn't enabled by default. See https://aka.ms/AAt8mw4.
// builder.Services
//     .AddApplicationInsightsTelemetryWorkerService()
//     .ConfigureFunctionsApplicationInsights();

var storageConfig = builder.Configuration
    .GetRequiredSection(nameof(AzureStorage))
    .Get<AzureStorage>()
    ?? throw new InvalidOperationException($"Configuration section '{nameof(AzureStorage)}' is not configured.");

var notificationUrl = builder.Configuration["NotificationUrl"]
    ?? throw new InvalidOperationException($"NotificationUrl is not configured.");

builder.Services.AddLogging();
builder.Services.AddHttpClient<NotificationClient>(client => client.BaseAddress = new Uri(notificationUrl));
builder.Services.AddAzureClients(azure =>
{
    azure.AddBlobServiceClient(storageConfig.ConnectionString)
        .ConfigureOptions(options =>
        {
            options.Retry.Mode = storageConfig.RetryOptions.Mode;
            options.Retry.MaxRetries = storageConfig.RetryOptions.MaxRetries;
            options.Retry.Delay = storageConfig.RetryOptions.Delay;
            options.Retry.MaxDelay = storageConfig.RetryOptions.MaxDelay;
            options.Retry.NetworkTimeout = storageConfig.RetryOptions.NetworkTimeout;
        });
});

builder.Services.AddSingleton(sp =>
{
    var service = sp.GetRequiredService<BlobServiceClient>();
    return service.GetBlobContainerClient(storageConfig.Container);
});

var host = builder.Build();
var container = host.Services.GetRequiredService<BlobContainerClient>();
await container.CreateIfNotExistsAsync();
await host.RunAsync();

file class AzureStorage
{
    public required string ConnectionString { get; init; }
    public required string Container { get; init; }
    public AzureStorageRetryOptions RetryOptions { get; init; } = new AzureStorageRetryOptions();
}

file class AzureStorageRetryOptions
{
    private static readonly RetryOptions _azureStorage = ClientOptions.Default.Retry;
    public RetryMode Mode { get; init; } = _azureStorage.Mode;
    public int MaxRetries { get; init; } = _azureStorage.MaxRetries;
    public TimeSpan Delay { get; init; } = _azureStorage.Delay;
    public TimeSpan MaxDelay { get; init; } = _azureStorage.MaxDelay;
    public TimeSpan NetworkTimeout { get; init; } = _azureStorage.NetworkTimeout;
}
