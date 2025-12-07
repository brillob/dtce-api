using Dtce.Identity.Models;

namespace Dtce.Identity;

public interface IUserService
{
    Task<UserRecord?> RegisterAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<UserRecord?> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<UserRecord?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ApiKeyRecord> GenerateApiKeyAsync(Guid userId, string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApiKeyRecord>> GetUserApiKeysAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> RevokeApiKeyAsync(Guid userId, Guid apiKeyId, CancellationToken cancellationToken = default);
    Task<bool> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);
}


