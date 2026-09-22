using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Vibe.Api.Security;

/// <summary>
/// Verifies a Google Sign-In id token before a VIBE session is issued.
/// </summary>
/// <remarks>
/// The PoE requires "Google Sign-In returning a verified identity token". The token
/// is checked against Google's tokeninfo endpoint, and the audience must match the
/// web client id the app was built with; an unverified email or an expired token is
/// refused. In production the same class would verify the JWKS signature locally to
/// avoid the extra round trip - the contract stays identical.
/// </remarks>
public class GoogleTokenValidator(HttpClient http, IConfiguration configuration, ILogger<GoogleTokenValidator> logger)
{
    private const string TokenInfoUrl = "https://oauth2.googleapis.com/tokeninfo";

    public async Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        try
        {
            var info = await http.GetFromJsonAsync<TokenInfo>(
                $"{TokenInfoUrl}?id_token={Uri.EscapeDataString(idToken)}",
                cancellationToken);

            if (info is null || string.IsNullOrWhiteSpace(info.Email))
            {
                return null;
            }

            var expectedAudience = configuration["Google:ServerClientId"];
            if (!string.IsNullOrWhiteSpace(expectedAudience) &&
                !string.Equals(info.Audience, expectedAudience, StringComparison.Ordinal))
            {
                logger.LogWarning("Google id token audience mismatch");
                return null;
            }

            if (info.EmailVerified is "false" or "False")
            {
                return null;
            }

            return new GoogleIdentity(info.Subject ?? info.Email, info.Email, info.Name ?? info.Email, info.Picture);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(error, "Google id token validation failed");
            return null;
        }
    }

    public record GoogleIdentity(string Subject, string Email, string DisplayName, string? PictureUri);

    private sealed record TokenInfo
    {
        [JsonPropertyName("sub")]
        public string? Subject { get; init; }

        [JsonPropertyName("email")]
        public string? Email { get; init; }

        [JsonPropertyName("email_verified")]
        public string? EmailVerified { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("picture")]
        public string? Picture { get; init; }

        [JsonPropertyName("aud")]
        public string? Audience { get; init; }
    }
}
