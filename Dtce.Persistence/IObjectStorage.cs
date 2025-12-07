namespace Dtce.Persistence;

public interface IObjectStorage
{
    Task<string> UploadFileAsync(string fileName, Stream fileStream, string contentType, CancellationToken cancellationToken = default);
    Task<string> GeneratePreSignedUrlAsync(string fileKey, TimeSpan expiration, CancellationToken cancellationToken = default);
    Task<Stream> DownloadFileAsync(string fileKey, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string fileKey, CancellationToken cancellationToken = default);
}

