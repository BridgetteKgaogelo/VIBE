using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Vibe.Api.Data;

namespace Vibe.Api.Security;

/// <summary>Issues the short lived access token and the refresh token the client stores.</summary>
public class JwtIssuer(IConfiguration configuration, TimeProvider clock)
{
    public const string IssuerKey = "Jwt:Issuer";
    public const string AudienceKey = "Jwt:Audience";
    public const string SigningKeyKey = "Jwt:SigningKey";

    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(12);

    public static SymmetricSecurityKey SigningKey(IConfiguration configuration)
    {
        var secret = configuration[SigningKeyKey];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            // Never ship without a key: the development key is long but obvious, and
            // Program.cs refuses to start in Production without a configured one.
            secret = "development-only-signing-key-change-me-please-32+";
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public AuthTokens Issue(User user)
    {
        var now = clock.GetUtcNow();
        var expires = now.Add(AccessTokenLifetime);

        var credentials = new SigningCredentials(SigningKey(configuration), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: configuration[IssuerKey] ?? "vibe-api",
            audience: configuration[AudienceKey] ?? "vibe-app",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("name", user.DisplayName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new AuthTokens(
            new JwtSecurityTokenHandler().WriteToken(token),
            GenerateRefreshToken(),
            expires.ToUnixTimeMilliseconds());
    }

    /// <summary>A random, opaque refresh token - it is stored hashed, not as JWT.</summary>
    public static string GenerateRefreshToken() => Domain.DecisionEngine.NewToken(48);
}

public record AuthTokens(string AccessToken, string RefreshToken, long ExpiresAtEpochMillis);
