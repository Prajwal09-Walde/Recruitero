using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace RecruitAI.IntegrationTests;

/// <summary>Generates test JWT tokens with the same secret used in appsettings.Testing.json</summary>
public static class TestJwtGenerator
{
    private const string Secret   = "REPLACE_WITH_32+_CHAR_SECRET_KEY_HERE!!";
    private const string Issuer   = "Recruitero";
    private const string Audience = "Recruitero.Clients";

    public static string Generate(string role, string userId = "test-user-id")
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Role, role)
        };

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
