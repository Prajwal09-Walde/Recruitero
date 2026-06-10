using RecruitAI.Domain.Common;

namespace RecruitAI.Domain.Entities;

/// <summary>
/// Represents a registered platform user (HRAdmin, Recruiter, or Viewer).
/// Passwords are stored as BCrypt hashes — never plain text.
/// </summary>
public class AppUser : BaseEntity
{
    public string FullName { get; private set; } = string.Empty;
    public string Email    { get; private set; } = string.Empty;
    /// <summary>BCrypt hash of the user's password.</summary>
    public string PasswordHash { get; private set; } = string.Empty;
    /// <summary>One of: HRAdmin | Recruiter | Viewer</summary>
    public string Role     { get; private set; } = string.Empty;
    public DateTime CreatedAt  { get; private set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; private set; }
    /// <summary>Opaque refresh token stored in MongoDB for server-side validation.</summary>
    public string? RefreshToken { get; private set; }
    public DateTime? RefreshTokenExpiry { get; private set; }

    // Required by MongoDB deserialization
    private AppUser() { }

    public AppUser(string fullName, string email, string passwordHash, string role)
    {
        FullName     = fullName.Trim();
        Email        = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;
        Role         = role;
    }

    public void RecordLogin() => LastLoginAt = DateTime.UtcNow;

    public void SetRefreshToken(string token, DateTime expiry)
    {
        RefreshToken       = token;
        RefreshTokenExpiry = expiry;
    }

    public void RevokeRefreshToken()
    {
        RefreshToken       = null;
        RefreshTokenExpiry = null;
    }
}
