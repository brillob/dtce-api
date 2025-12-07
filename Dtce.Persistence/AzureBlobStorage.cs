using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;

namespace Dtce.Persistence;

public class AzureBlobStorage : IObjectStorage
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;
    private readonly ILogger<AzureBlobStorage> _logger;

    public AzureBlobStorage(string connectionString, string containerName, ILogger<AzureBlobStorage> logger)
    {
        _blobServiceClient = new BlobServiceClient(connectionString);
        _containerName = containerName;
        _logger = logger;
    }

    public async Task<string> UploadFileAsync(string fileName, Stream fileStream, string contentType, CancellationToken cancellationToken = default)
    {
        var containerClient = await GetContainerClientAsync(cancellationToken);
        var blobClient = containerClient.GetBlobClient(fileName);

        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType
            }
        };

        await blobClient.UploadAsync(fileStream, uploadOptions, cancellationToken);
        _logger.LogInformation("Uploaded file {FileName} to blob storage", fileName);

        return blobClient.Uri.ToString();
    }

    public async Task<string> GeneratePreSignedUrlAsync(string fileKey, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        var containerClient = await GetContainerClientAsync(cancellationToken);
        var blobClient = containerClient.GetBlobClient(fileKey);

        if (!await blobClient.ExistsAsync(cancellationToken: cancellationToken))
        {
            throw new FileNotFoundException($"File {fileKey} not found in blob storage");
        }

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _containerName,
            BlobName = fileKey,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(expiration)
        };

        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sasUri = blobClient.GenerateSasUri(sasBuilder);
        _logger.LogInformation("Generated pre-signed URL for {FileKey}", fileKey);

        return sasUri.ToString();
    }

    public async Task<Stream> DownloadFileAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        var containerClient = await GetContainerClientAsync(cancellationToken);
        var blobClient = containerClient.GetBlobClient(fileKey);

        if (!await blobClient.ExistsAsync(cancellationToken: cancellationToken))
        {
            throw new FileNotFoundException($"File {fileKey} not found in blob storage");
        }

        var memoryStream = new MemoryStream();
        await blobClient.DownloadToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        return memoryStream;
    }

    public async Task DeleteFileAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        var containerClient = await GetContainerClientAsync(cancellationToken);
        var blobClient = containerClient.GetBlobClient(fileKey);

        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        _logger.LogInformation("Deleted file {FileKey} from blob storage", fileKey);
    }

    private async Task<BlobContainerClient> GetContainerClientAsync(CancellationToken cancellationToken)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        return containerClient;
    }
}

