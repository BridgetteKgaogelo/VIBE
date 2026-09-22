using System.Security.Claims;
using Vibe.Api.Domain;

namespace Vibe.Api.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in member's id, or null for an anonymous caller.</summary>
    public static Guid? UserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");

        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Used by endpoints behind <c>RequireAuthorization()</c>.</summary>
    public static Guid RequireUserId(this ClaimsPrincipal principal) =>
        principal.UserId() ?? throw new VibeException(StatusCodes.Status401Unauthorized, "Sign in to continue.");
}
