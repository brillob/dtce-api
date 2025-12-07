using Dtce.AzureFunctions;
using Dtce.Identity;
using Dtce.Identity.Stores;
using Dtce.JobQueue;
using Dtce.Persistence;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Get configuration
var serviceBusConnectionString = builder.Configuration["Azure:ServiceBus:ConnectionString"] 
    ?? throw new InvalidOperationException("Azure:ServiceBus:ConnectionString is required");
var blobStorageConnectionString = builder.Configuration["Azure:Storage:ConnectionString"] 
    ?? throw new InvalidOperationException("Azure:Storage:ConnectionString is required");
var tableStorageConnectionString = builder.Configuration["Azure:Storage:ConnectionString"] 
    ?? throw new InvalidOperationException("Azure:Storage:ConnectionString is required");
var containerName = builder.Configuration["Azure:Storage:ContainerName"] ?? "dtce-documents";

// Register Azure services
builder.Services.AddSingleton<IMessageProducer>(sp => 
    new AzureServiceBusProducer(serviceBusConnectionString, sp.GetRequiredService<ILogger<AzureServiceBusProducer>>()));

builder.Services.AddSingleton<IObjectStorage>(sp => 
    new AzureBlobStorage(blobStorageConnectionString, containerName, sp.GetRequiredService<ILogger<AzureBlobStorage>>()));

builder.Services.AddSingleton<IJobStatusRepository>(sp => 
    new AzureTableStorageJobRepository(tableStorageConnectionString, sp.GetRequiredService<ILogger<AzureTableStorageJobRepository>>()));

builder.Services.AddSingleton<IUserStore>(_ => new AzureTableUserStore(tableStorageConnectionString));
builder.Services.AddSingleton<IUserService, UserService>();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();

