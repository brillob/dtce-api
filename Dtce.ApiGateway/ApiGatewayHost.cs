using Dtce.Identity;
using Dtce.Identity.Stores;
using Dtce.JobQueue;
using Dtce.Persistence;
using Dtce.Infrastructure.Local;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Temporarily disable Swagger to avoid OpenAPI type loading issues
// builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5091", "https://localhost:7264")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var configuration = builder.Configuration;
var platformMode = configuration["Platform:Mode"] ?? "Prod";
var serviceBusConnectionString = configuration["Azure:ServiceBus:ConnectionString"];
var storageConnectionString = configuration["Azure:Storage:ConnectionString"];
var containerName = configuration["Azure:Storage:ContainerName"] ?? "dtce-documents";

var useAzure = string.Equals(platformMode, "Prod", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(serviceBusConnectionString)
               && !string.IsNullOrWhiteSpace(storageConnectionString);

if (useAzure)
{
    builder.Services.AddSingleton<IMessageProducer>(sp =>
        new AzureServiceBusProducer(serviceBusConnectionString!, sp.GetRequiredService<ILogger<AzureServiceBusProducer>>()));

    builder.Services.AddSingleton<IObjectStorage>(sp =>
        new AzureBlobStorage(storageConnectionString!, containerName, sp.GetRequiredService<ILogger<AzureBlobStorage>>()));

    builder.Services.AddSingleton<IJobStatusRepository>(sp =>
        new AzureTableStorageJobRepository(storageConnectionString!, sp.GetRequiredService<ILogger<AzureTableStorageJobRepository>>()));

    builder.Services.AddSingleton<IUserStore>(_ => new AzureTableUserStore(storageConnectionString!));
}
else
{
    builder.Services.Configure<FileQueueOptions>(configuration.GetSection("Messaging"));
    builder.Services.Configure<FileSystemStorageOptions>(configuration.GetSection("Storage"));

    builder.Services.AddSingleton<IMessageProducer, FileQueueMessageProducer>();
    builder.Services.AddSingleton<IJobStatusRepository, FileSystemJobStatusRepository>();
    builder.Services.AddSingleton<IObjectStorage, FileSystemObjectStorage>();
    builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();
}

builder.Services.AddSingleton<IUserService, UserService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
// Temporarily disable Swagger to avoid OpenAPI type loading issues
// if (app.Environment.IsDevelopment())
// {
//     app.UseSwagger();
//     app.UseSwaggerUI();
// }

app.UseCors();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
