using Dtce.AnalysisEngine;
using Dtce.AnalysisEngine.Services;
using Dtce.Infrastructure.Local;
using Dtce.JobQueue;
using Dtce.Persistence;
using Microsoft.Extensions.Configuration;

var builder = Host.CreateApplicationBuilder(args);

var platformMode = builder.Configuration["Platform:Mode"] ?? "Prod";
var useAzure = string.Equals(platformMode, "Prod", StringComparison.OrdinalIgnoreCase);

if (useAzure)
{
    var serviceBusConnectionString = builder.Configuration["Azure:ServiceBus:ConnectionString"] 
        ?? throw new InvalidOperationException("Azure:ServiceBus:ConnectionString is required");
    var blobStorageConnectionString = builder.Configuration["Azure:Storage:ConnectionString"] 
        ?? throw new InvalidOperationException("Azure:Storage:ConnectionString is required");
    var tableStorageConnectionString = builder.Configuration["Azure:Storage:ConnectionString"] 
        ?? throw new InvalidOperationException("Azure:Storage:ConnectionString is required");
    var containerName = builder.Configuration["Azure:Storage:ContainerName"] ?? "dtce-documents";

    builder.Services.AddSingleton<IMessageConsumer>(sp => 
        new AzureServiceBusConsumer(serviceBusConnectionString, sp.GetRequiredService<ILogger<AzureServiceBusConsumer>>()));
    builder.Services.AddSingleton<IMessageProducer>(sp => 
        new AzureServiceBusProducer(serviceBusConnectionString, sp.GetRequiredService<ILogger<AzureServiceBusProducer>>()));

    builder.Services.AddSingleton<IObjectStorage>(sp => 
        new AzureBlobStorage(blobStorageConnectionString, containerName, sp.GetRequiredService<ILogger<AzureBlobStorage>>()));

    builder.Services.AddSingleton<IJobStatusRepository>(sp => 
        new AzureTableStorageJobRepository(tableStorageConnectionString, sp.GetRequiredService<ILogger<AzureTableStorageJobRepository>>()));
}
else
{
    builder.Services.Configure<FileQueueOptions>(builder.Configuration.GetSection("Messaging"));
    builder.Services.Configure<FileSystemStorageOptions>(builder.Configuration.GetSection("Storage"));

    builder.Services.AddSingleton<IMessageConsumer, FileQueueMessageConsumer>();
    builder.Services.AddSingleton<IMessageProducer, FileQueueMessageProducer>();
    builder.Services.AddSingleton<IObjectStorage, FileSystemObjectStorage>();
    builder.Services.AddSingleton<IJobStatusRepository, FileSystemJobStatusRepository>();
}

// Register analysis services
builder.Services.AddSingleton<INlpAnalyzer, NlpAnalyzer>();
builder.Services.AddSingleton<IComputerVisionAnalyzer, ComputerVisionAnalyzer>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

