using MongoDB.Driver;
using RecruitAI.Domain.Entities;
using RecruitAI.Infrastructure.Persistence;

namespace RecruitAI.API.Services;

/// <summary>
/// Handles user registration and credential validation against MongoDB.
/// Passwords are always stored as BCrypt hashes (work factor 12).
/// Refresh tokens are stored hashed for server-side validation.
/// </summary>
public class UserService(MongoDbContext db) : IUserService
{
    private readonly IMongoCollection<AppUser> _users = db.Users;

    // ── Register ─────────────────────────────────────────────────────────────────
    public async Task<UserRecord?> RegisterAsync(
        string fullName, string email, string password, string role,
        CancellationToken ct = default)
    {
        var normalizedEmail = Normalize(email);

        var exists = await _users
            .Find(u => u.Email == normalizedEmail)
            .AnyAsync(ct);

        if (exists) return null; // email already taken

        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        var user = new AppUser(fullName, normalizedEmail, hash, role);

        await _users.InsertOneAsync(user, cancellationToken: ct);
        return Project(user);
    }

    // ── Validate credentials ──────────────────────────────────────────────────────
    public async Task<UserRecord?> ValidateAsync(
        string email, string password,
        CancellationToken ct = default)
    {
        var user = await FindByEmail(email, ct);
        if (user is null) return null;
        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) return null;

        user.RecordLogin();
        await Save(user, ct);
        return Project(user);
    }

    // ── Refresh token: save ───────────────────────────────────────────────────────
    public async Task SaveRefreshTokenAsync(
        string email, string refreshToken, DateTime expiry,
        CancellationToken ct = default)
    {
        var user = await FindByEmail(email, ct);
        if (user is null) return;

        // Hash the refresh token before storing (like a password)
        var tokenHash = BCrypt.Net.BCrypt.HashPassword(refreshToken, workFactor: 8);
        user.SetRefreshToken(tokenHash, expiry);
        await Save(user, ct);
    }

    // ── Refresh token: validate ───────────────────────────────────────────────────
    public async Task<UserRecord?> ValidateRefreshTokenAsync(
        string email, string refreshToken,
        CancellationToken ct = default)
    {
        var user = await FindByEmail(email, ct);
        if (user is null) return null;
        if (string.IsNullOrEmpty(user.RefreshToken)) return null;
        if (user.RefreshTokenExpiry < DateTime.UtcNow) return null; // expired

        if (!BCrypt.Net.BCrypt.Verify(refreshToken, user.RefreshToken)) return null;

        return Project(user);
    }

    // ── Refresh token: revoke (logout) ────────────────────────────────────────────
    public async Task RevokeRefreshTokenAsync(string email, CancellationToken ct = default)
    {
        var user = await FindByEmail(email, ct);
        if (user is null) return;

        user.RevokeRefreshToken();
        await Save(user, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────
    private async Task<AppUser?> FindByEmail(string email, CancellationToken ct)
    {
        var normalized = Normalize(email);
        return await _users.Find(u => u.Email == normalized).FirstOrDefaultAsync(ct);
    }

    private Task Save(AppUser user, CancellationToken ct) =>
        _users.ReplaceOneAsync(u => u.Id == user.Id, user, cancellationToken: ct);

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static UserRecord Project(AppUser u) =>
        new(u.Id.ToString(), u.FullName, u.Email, u.Role);
}
