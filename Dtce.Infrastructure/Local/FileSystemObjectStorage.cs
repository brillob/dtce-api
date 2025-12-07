using Dtce.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dtce.Infrastructure.Local;

public class FileSystemObjectStorage : IObjectStorage
{
    private readonly ILogger<FileSystemObjectStorage> _logger;
    private readonly string _rootPath;

    public FileSystemObjectStorage(
        ILogger<FileSystemObjectStorage> logger,
        IOptions<FileSystemStorageOptions> options)
    {
        _logger = logger;
        _rootPath = options.Value.RootPath;
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> UploadFileAsync(string fileName, Stream fileStream, string contentType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name cannot be empty.", nameof(fileName));
        }

        var fullPath = ResolvePath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);
        await fileStream.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);

        _logger.LogInformation("Stored file {FileName} at {Path}", fileName, fullPath);
        return new Uri(fullPath).AbsoluteUri;
    }

    public Task<string> GeneratePreSignedUrlAsync(string fileKey, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(fileKey);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File {fileKey} not found", fullPath);
        }

        // For local file system, return an HTTP URL pointing to the API Gateway file endpoint
        // The API Gateway base URL should be configured, but for now we'll use a default
        // In production, this should come from configuration
        var baseUrl = Environment.GetEnvironmentVariable("API_GATEWAY_BASE_URL") 
            ?? "http://localhost:5017";
        
        // Encode each path segment separately to preserve slashes for routing
        var pathSegments = fileKey.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var encodedSegments = pathSegments.Select(Uri.EscapeDataString);
        var encodedPath = string.Join("/", encodedSegments);
        // Default to inline viewing (not download)
        var url = $"{baseUrl}/api/v1/jobs/files/{encodedPath}";
        
        return Task.FromResult(url);
    }

    public Task<Stream> DownloadFileAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(fileKey);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File {fileKey} not found", fullPath);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteFileAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(fileKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("Deleted file {FileKey}", fileKey);
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(string fileKey)
    {
        // Handle file URIs (e.g., file:///C:/path/to/file)
        if (Uri.TryCreate(fileKey, UriKind.Absolute, out var uri) && uri.Scheme == "file")
        {
            // On Windows, LocalPath might have a leading slash, so we need to normalize it
            var localPath = uri.LocalPath;
            if (Path.IsPathRooted(localPath))
            {
                // If the path is within our root path, use it directly
                var normalizedLocalPath = Path.GetFullPath(localPath);
                if (normalizedLocalPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    return normalizedLocalPath;
                }
            }
            // If it's a file URI but not in our root, try to extract relative path
            // This shouldn't normally happen, but handle it gracefully
            var rootUri = new Uri(_rootPath + Path.DirectorySeparatorChar);
            var relativePath = rootUri.MakeRelativeUri(uri).ToString().Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        }

        // Handle relative paths (e.g., documents/jobId/fileName)
        var sanitized = string.Join(Path.DirectorySeparatorChar,
            fileKey.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return Path.GetFullPath(Path.Combine(_rootPath, sanitized));
    }
}


