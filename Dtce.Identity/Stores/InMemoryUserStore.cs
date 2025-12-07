using System.Collections.Concurrent;
using Dtce.Identity.Models;

namespace Dtce.Identity.Stores;

public class InMemoryUserStore : IUserStore
{
    private readonly ConcurrentDictionary<Guid, UserRecord> _users = new();
    private readonly ConcurrentDictionary<Guid, ApiKeyRecord> _apiKeys = new();
    private readonly ConcurrentDictionary<string, Guid> _emailIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Guid> _apiKeyIndex = new(StringComparer.Ordinal);

    public Task CreateAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        if (!_users.TryAdd(user.Id, user))
        {
            throw new InvalidOperationException("Failed to add user");
        }

        if (!_emailIndex.TryAdd(user.Email, user.Id))
        {
            _users.TryRemove(user.Id, out _);
            throw new InvalidOperationException("Failed to index user email");
        }

        return Task.CompletedTask;
    }

    public Task<UserRecord?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (_emailIndex.TryGetValue(email, out var userId) && _users.TryGetValue(userId, out var user))
        {
            return Task.FromResult<UserRecord?>(user);
        }

        return Task.FromResult<UserRecord?>(null);
    }

    public Task<UserRecord?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        _users.TryGetValue(userId, out var user);
        return Task.FromResult<UserRecord?>(user);
    }

    public Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        _users[user.Id] = user;
        _emailIndex[user.Email] = user.Id;
        return Task.CompletedTask;
    }

    public Task<ApiKeyRecord> CreateApiKeyAsync(ApiKeyRecord apiKey, CancellationToken cancellationToken = default)
    {
        if (!_apiKeys.TryAdd(apiKey.Id, apiKey))
        {
            throw new InvalidOperationException("Failed to create API key");
        }

        if (!_apiKeyIndex.TryAdd(apiKey.Key, apiKey.Id))
        {
            _apiKeys.TryRemove(apiKey.Id, out _);
            throw new InvalidOperationException("Failed to index API key");
        }

        return Task.FromResult(apiKey);
    }

    public Task<ApiKeyRecord?> GetApiKeyByIdAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        _apiKeys.TryGetValue(apiKeyId, out var apiKey);
        return Task.FromResult<ApiKeyRecord?>(apiKey);
    }

    public Task<ApiKeyRecord?> GetApiKeyByKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (_apiKeyIndex.TryGetValue(apiKey, out var apiKeyId) && _apiKeys.TryGetValue(apiKeyId, out var record))
        {
            return Task.FromResult<ApiKeyRecord?>(record);
        }

        return Task.FromResult<ApiKeyRecord?>(null);
    }

    public Task<IReadOnlyList<ApiKeyRecord>> GetApiKeysByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var keys = _apiKeys.Values.Where(k => k.UserId == userId).ToList();
        return Task.FromResult<IReadOnlyList<ApiKeyRecord>>(keys);
    }

    public Task UpdateApiKeyAsync(ApiKeyRecord apiKey, CancellationToken cancellationToken = default)
    {
        _apiKeys[apiKey.Id] = apiKey;
        _apiKeyIndex[apiKey.Key] = apiKey.Id;
        return Task.CompletedTask;
    }
}


