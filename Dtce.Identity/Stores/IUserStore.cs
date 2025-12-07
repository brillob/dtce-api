using Dtce.Identity.Models;

namespace Dtce.Identity.Stores;

public interface IUserStore
{
    Task<UserRecord?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserRecord?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task CreateAsync(UserRecord user, CancellationToken cancellationToken = default);
    Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default);

    Task<ApiKeyRecord> CreateApiKeyAsync(ApiKeyRecord apiKey, CancellationToken cancellationToken = default);
    Task<ApiKeyRecord?> GetApiKeyByIdAsync(Guid apiKeyId, CancellationToken cancellationToken = default);
    Task<ApiKeyRecord?> GetApiKeyByKeyAsync(string apiKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApiKeyRecord>> GetApiKeysByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task UpdateApiKeyAsync(ApiKeyRecord apiKey, CancellationToken cancellationToken = default);
}


