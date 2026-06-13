using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using RecruitAI.API.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using RecruitAI.Application.Interfaces;

namespace RecruitAI.API.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(IConfiguration configuration, IUserService userService, IEmailService emailService, ILogger<AuthController> logger) : ControllerBase
{
    // ── Token lifetimes ───────────────────────────────────────────────────────────
    private int AccessTokenMinutes =>
        int.Parse(configuration["Jwt:ExpiryMinutes"] ?? "60");

    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    // ── Register ──────────────────────────────────────────────────────────────────
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.Role))
        {
            return BadRequest(Error400("FullName, Email, Password, and Role are required."));
        }

        var validRoles = new[] { "HRAdmin", "Recruiter", "Viewer" };
        if (!validRoles.Contains(request.Role))
            return BadRequest(Error400("Role must be one of: HRAdmin, Recruiter, Viewer."));

        if (request.Password.Length < 6)
            return BadRequest(Error400("Password must be at least 6 characters."));

        var user = await userService.RegisterAsync(request.FullName, request.Email, request.Password, request.Role, ct);
        if (user is null)
        {
            return Conflict(new ProblemDetails
            {
                Status = 409,
                Title  = "Email already registered",
                Detail = "An account with that email address already exists. Please sign in instead."
            });
        }

        return Ok(await IssueTokens(user, ct));
    }

    // ── Login ─────────────────────────────────────────────────────────────────────
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(Error400("Email and Password are required."));

        var user = await userService.ValidateAsync(request.Email, request.Password, ct);
        if (user is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = 401,
                Title  = "Invalid credentials",
                Detail = "The email or password you entered is incorrect."
            });
        }

        return Ok(await IssueTokens(user, ct));
    }

    // ── Refresh ───────────────────────────────────────────────────────────────────
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(Error400("Email and RefreshToken are required."));

        var user = await userService.ValidateRefreshTokenAsync(request.Email, request.RefreshToken, ct);
        if (user is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = 401,
                Title  = "Invalid or expired refresh token",
                Detail = "Your session has expired. Please sign in again."
            });
        }

        return Ok(await IssueTokens(user, ct));
    }

    // ── Logout ────────────────────────────────────────────────────────────────────
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email)
                 ?? User.FindFirstValue(JwtRegisteredClaimNames.Email);

        if (!string.IsNullOrEmpty(email))
            await userService.RevokeRefreshTokenAsync(email, ct);

        return NoContent();
    }

    // ── Me (current user profile) ─────────────────────────────────────────────────
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var email    = User.FindFirstValue(JwtRegisteredClaimNames.Email) ?? "";
        var role     = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var fullName = User.FindFirstValue("name") ?? "";
        return Ok(new { email, role, fullName });
    }

    // ── Forgot Password ───────────────────────────────────────────────────────────
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(Error400("Email is required."));

        var token = await userService.GeneratePasswordResetTokenAsync(request.Email, ct);
        if (token is not null)
        {
            var resetLink = $"http://localhost:3000/reset-password?email={Uri.EscapeDataString(request.Email)}&token={Uri.EscapeDataString(token)}";
            var subject = "Reset Your RecruitAI Password";
            var body = $"Please use the link below to verify your email and reset your password:\n\n{resetLink}";
            
            await emailService.SendEmailAsync(request.Email, subject, body, ct);
        }

        return Ok(new { Message = "If your email is registered in our system, a password reset link has been sent to it." });
    }

    // ── Reset Password ────────────────────────────────────────────────────────────
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || 
            string.IsNullOrWhiteSpace(request.Token) || 
            string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(Error400("Email, Token, and NewPassword are required."));
        }

        if (request.NewPassword.Length < 6)
            return BadRequest(Error400("Password must be at least 6 characters."));

        var success = await userService.ResetPasswordAsync(request.Email, request.Token, request.NewPassword, ct);
        if (!success)
        {
            return BadRequest(new ProblemDetails
            {
                Status = 400,
                Title = "Invalid or expired token",
                Detail = "The password reset token is invalid or has expired. Please request a new password reset."
            });
        }

        return Ok(new { Message = "Password has been reset successfully." });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────
    private async Task<AuthResponse> IssueTokens(UserRecord user, CancellationToken ct)
    {
        // Generate access token (JWT)
        var accessToken = GenerateAccessToken(user.Email, user.Role, user.FullName);

        // Generate refresh token (opaque random, stored hashed in DB)
        var rawRefreshToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) +
                              Convert.ToBase64String(Guid.NewGuid().ToByteArray()); // ~43 chars, URL-safe

        var expiry = DateTime.UtcNow.Add(RefreshTokenLifetime);
        await userService.SaveRefreshTokenAsync(user.Email, rawRefreshToken, expiry, ct);

        return new AuthResponse(accessToken, rawRefreshToken, user.Email, user.Role, user.FullName);
    }

    private string GenerateAccessToken(string email, string role, string name)
    {
        var secret = configuration["Jwt:Secret"] ?? "REPLACE_WITH_32+_CHAR_SECRET_KEY_HERE!!";
        var key    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds  = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   email),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Role,               role),
            new Claim("name",                        name),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer:             configuration["Jwt:Issuer"]   ?? "Recruitero",
            audience:           configuration["Jwt:Audience"] ?? "Recruitero.Clients",
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(AccessTokenMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static ProblemDetails Error400(string detail) =>
        new() { Status = 400, Title = "Invalid request", Detail = detail };
}

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Email, string Password, string FullName, string Role);
public record RefreshRequest(string Email, string RefreshToken);
public record AuthResponse(string Token, string RefreshToken, string Email, string Role, string FullName);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Token, string NewPassword);
