using Azure;
using Azure.Data.Tables;
using Dtce.Identity.Models;

namespace Dtce.Identity.Stores;

public class AzureTableUserStore : IUserStore
{
    private const string UserPartitionKey = "USER";
    private readonly TableClient _usersTable;
    private readonly TableClient _apiKeysTable;

    public AzureTableUserStore(string connectionString, string usersTableName = "Users", string apiKeysTableName = "ApiKeys")
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        var serviceClient = new TableServiceClient(connectionString);
        _usersTable = serviceClient.GetTableClient(usersTableName);
        _apiKeysTable = serviceClient.GetTableClient(apiKeysTableName);

        _usersTable.CreateIfNotExists();
        _apiKeysTable.CreateIfNotExists();
    }

    public async Task CreateAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        var entity = MapToEntity(user);
        await _usersTable.AddEntityAsync(entity, cancellationToken);
    }

    public async Task<UserRecord?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        var filter = $"PartitionKey eq '{UserPartitionKey}' and NormalizedEmail eq '{EscapeFilterValue(normalizedEmail)}'";

        await foreach (var entity in _usersTable.QueryAsync<UserEntity>(filter: filter, maxPerPage: 1, cancellationToken: cancellationToken))
        {
            return MapToModel(entity);
        }

        return null;
    }

    public async Task<UserRecord?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _usersTable.GetEntityAsync<UserEntity>(UserPartitionKey, userId.ToString(), cancellationToken: cancellationToken);
            return MapToModel(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        var entity = MapToEntity(user);
        await _usersTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<ApiKeyRecord> CreateApiKeyAsync(ApiKeyRecord apiKey, CancellationToken cancellationToken = default)
    {
        var entity = MapToEntity(apiKey);
        await _apiKeysTable.AddEntityAsync(entity, cancellationToken);
        return apiKey;
    }

    public async Task<ApiKeyRecord?> GetApiKeyByIdAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        var filter = $"RowKey eq '{EscapeFilterValue(apiKeyId.ToString())}'";

        await foreach (var entity in _apiKeysTable.QueryAsync<ApiKeyEntity>(filter: filter, maxPerPage: 1, cancellationToken: cancellationToken))
        {
            return MapToModel(entity);
        }

        return null;
    }

    public async Task<ApiKeyRecord?> GetApiKeyByKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        var filter = $"ApiKey eq '{EscapeFilterValue(apiKey)}'";

        await foreach (var entity in _apiKeysTable.QueryAsync<ApiKeyEntity>(filter: filter, maxPerPage: 1, cancellationToken: cancellationToken))
        {
            return MapToModel(entity);
        }

        return null;
    }

    public async Task<IReadOnlyList<ApiKeyRecord>> GetApiKeysByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var filter = $"PartitionKey eq '{EscapeFilterValue(userId.ToString())}'";
        var results = new List<ApiKeyRecord>();

        await foreach (var entity in _apiKeysTable.QueryAsync<ApiKeyEntity>(filter: filter, cancellationToken: cancellationToken))
        {
            results.Add(MapToModel(entity));
        }

        return results;
    }

    public async Task UpdateApiKeyAsync(ApiKeyRecord apiKey, CancellationToken cancellationToken = default)
    {
        var entity = MapToEntity(apiKey);
        await _apiKeysTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string EscapeFilterValue(string value) => value.Replace("'", "''");

    private static UserEntity MapToEntity(UserRecord user) => new()
    {
        PartitionKey = UserPartitionKey,
        RowKey = user.Id.ToString(),
        Email = user.Email,
        NormalizedEmail = NormalizeEmail(user.Email),
        PasswordHash = user.PasswordHash,
        CreatedAt = user.CreatedAt,
        LastLoginAt = user.LastLoginAt,
        IsActive = user.IsActive
    };

    private static UserRecord MapToModel(UserEntity entity) => new()
    {
        Id = Guid.Parse(entity.RowKey),
        Email = entity.Email,
        PasswordHash = entity.PasswordHash,
        CreatedAt = entity.CreatedAt.UtcDateTime,
        LastLoginAt = entity.LastLoginAt?.UtcDateTime,
        IsActive = entity.IsActive
    };

    private static ApiKeyEntity MapToEntity(ApiKeyRecord apiKey) => new()
    {
        PartitionKey = apiKey.UserId.ToString(),
        RowKey = apiKey.Id.ToString(),
        ApiKey = apiKey.Key,
        Name = apiKey.Name,
        CreatedAt = apiKey.CreatedAt,
        RevokedAt = apiKey.RevokedAt,
        IsActive = apiKey.IsActive
    };

    private static ApiKeyRecord MapToModel(ApiKeyEntity entity) => new()
    {
        Id = Guid.Parse(entity.RowKey),
        UserId = Guid.Parse(entity.PartitionKey),
        Key = entity.ApiKey,
        Name = entity.Name,
        CreatedAt = entity.CreatedAt.UtcDateTime,
        RevokedAt = entity.RevokedAt?.UtcDateTime,
        IsActive = entity.IsActive
    };

    private class UserEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string Email { get; set; } = string.Empty;
        public string NormalizedEmail { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastLoginAt { get; set; }
        public bool IsActive { get; set; }
    }

    private class ApiKeyEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string ApiKey { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? RevokedAt { get; set; }
        public bool IsActive { get; set; }
    }
}


