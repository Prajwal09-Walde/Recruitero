namespace RecruitAI.API.Services;

public interface IUserService
{
    /// <summary>
    /// Registers a new user. Returns null if the email is already taken.
    /// </summary>
    Task<UserRecord?> RegisterAsync(string fullName, string email, string password, string role, CancellationToken ct = default);

    /// <summary>
    /// Validates email + password credentials. Returns user on success, null on failure.
    /// </summary>
    Task<UserRecord?> ValidateAsync(string email, string password, CancellationToken ct = default);

    /// <summary>
    /// Saves a new refresh token for the user (overwrites any existing one).
    /// </summary>
    Task SaveRefreshTokenAsync(string email, string refreshToken, DateTime expiry, CancellationToken ct = default);

    /// <summary>
    /// Validates a refresh token. Returns the user if valid and not expired, null otherwise.
    /// </summary>
    Task<UserRecord?> ValidateRefreshTokenAsync(string email, string refreshToken, CancellationToken ct = default);

    /// <summary>
    /// Revokes the stored refresh token for the user (logout).
    /// </summary>
    Task RevokeRefreshTokenAsync(string email, CancellationToken ct = default);
}

/// <summary>Lightweight projection returned by IUserService — avoids leaking PasswordHash.</summary>
public record UserRecord(string Id, string FullName, string Email, string Role);
