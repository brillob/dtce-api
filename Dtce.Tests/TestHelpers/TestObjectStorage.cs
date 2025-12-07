using Dtce.Persistence;

namespace Dtce.Tests.TestHelpers;

internal sealed class TestObjectStorage : IObjectStorage
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _contentTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _uploadedKeys = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> UploadedKeys => _uploadedKeys;

    public void SeedFile(string key, byte[] data, string contentType)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(contentType);

        _files[key] = data;
        _contentTypes[key] = contentType;
    }

    public void SeedFileFromPath(string key, string path, string contentType)
    {
        ArgumentNullException.ThrowIfNull(path);
        var data = File.ReadAllBytes(path);
        SeedFile(key, data, contentType);
    }

    public Task<string> UploadFileAsync(string fileName, Stream fileStream, string contentType, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        fileStream.CopyTo(buffer);

        _files[fileName] = buffer.ToArray();
        _contentTypes[fileName] = contentType;
        _uploadedKeys.Add(fileName);

        return Task.FromResult($"https://local.test/{Uri.EscapeDataString(fileName)}");
    }

    public Task<string> GeneratePreSignedUrlAsync(string fileKey, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        if (!_files.ContainsKey(fileKey))
        {
            throw new FileNotFoundException($"File {fileKey} not found in test storage.", fileKey);
        }

        return Task.FromResult($"https://local.test/{Uri.EscapeDataString(fileKey)}?expires={DateTime.UtcNow.Add(expiration):O}");
    }

    public Task<Stream> DownloadFileAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        if (!_files.TryGetValue(fileKey, out var bytes))
        {
            throw new FileNotFoundException($"File {fileKey} not found in test storage.", fileKey);
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task DeleteFileAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        _files.Remove(fileKey);
        _contentTypes.Remove(fileKey);
        _uploadedKeys.Remove(fileKey);
        return Task.CompletedTask;
    }
}


