using System.Security.Cryptography;
using BCrypt.Net;
using Dtce.Identity.Models;
using Dtce.Identity.Stores;
using Microsoft.Extensions.Logging;

namespace Dtce.Identity;

public class UserService : IUserService
{
    private readonly IUserStore _store;
    private readonly ILogger<UserService>? _logger;

    public UserService(IUserStore store, ILogger<UserService>? logger = null)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<UserRecord?> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Attempting to register user {Email}", email);

        var existing = await _store.GetByEmailAsync(email, cancellationToken);
        if (existing != null)
        {
            _logger?.LogWarning("Registration failed for {Email}: user already exists", email);
            return null;
        }

        var user = new UserRecord
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        await _store.CreateAsync(user, cancellationToken);
        _logger?.LogInformation("Created user {UserId}", user.Id);
        return user;
    }

    public async Task<UserRecord?> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await _store.GetByEmailAsync(email, cancellationToken);
        if (user == null || !user.IsActive)
        {
            _logger?.LogWarning("Login failed for {Email}", email);
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            _logger?.LogWarning("Invalid password for {Email}", email);
            return null;
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _store.UpdateAsync(user, cancellationToken);

        _logger?.LogInformation("User {UserId} logged in", user.Id);
        return user;
    }

    public Task<UserRecord?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _store.GetByIdAsync(userId, cancellationToken);

    public async Task<ApiKeyRecord> GenerateApiKeyAsync(Guid userId, string name, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Generating API key for user {UserId}", userId);

        var apiKey = new ApiKeyRecord
        {
            UserId = userId,
            Name = name,
            Key = await GenerateUniqueApiKeyAsync(cancellationToken),
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        return await _store.CreateApiKeyAsync(apiKey, cancellationToken);
    }

    public async Task<IReadOnlyList<ApiKeyRecord>> GetUserApiKeysAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var keys = await _store.GetApiKeysByUserAsync(userId, cancellationToken);
        return keys
            .Where(k => k.IsActive && k.RevokedAt == null)
            .OrderByDescending(k => k.CreatedAt)
            .ToList();
    }

    public async Task<bool> RevokeApiKeyAsync(Guid userId, Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        var apiKey = await _store.GetApiKeyByIdAsync(apiKeyId, cancellationToken);
        if (apiKey == null || apiKey.UserId != userId)
        {
            _logger?.LogWarning("Failed to revoke API key {ApiKeyId} for user {UserId}", apiKeyId, userId);
            return false;
        }

        apiKey.IsActive = false;
        apiKey.RevokedAt = DateTime.UtcNow;
        await _store.UpdateApiKeyAsync(apiKey, cancellationToken);

        _logger?.LogInformation("Revoked API key {ApiKeyId} for user {UserId}", apiKeyId, userId);
        return true;
    }

    public async Task<bool> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        var record = await _store.GetApiKeyByKeyAsync(apiKey, cancellationToken);
        var isValid = record is { IsActive: true, RevokedAt: null };

        if (!isValid)
        {
            _logger?.LogWarning("Invalid API key attempted");
        }

        return isValid;
    }

    private async Task<string> GenerateUniqueApiKeyAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var key = GenerateSecureKey();
            if (await _store.GetApiKeyByKeyAsync(key, cancellationToken) == null)
            {
                return key;
            }
        }

        throw new InvalidOperationException("Failed to generate a unique API key after multiple attempts.");
    }

    private static string GenerateSecureKey()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}


