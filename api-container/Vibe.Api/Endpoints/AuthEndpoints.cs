using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Vibe.Api.Contracts;
using Vibe.Api.Data;
using Vibe.Api.Infrastructure;
using Vibe.Api.Security;

namespace Vibe.Api.Endpoints;

public static class AuthEndpoints
{
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Authentication");

        group.MapPost("/register", async (
            RegisterRequest request,
            VibeDbContext db,
            JwtIssuer jwt,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var problem = Validators.DisplayName(request.DisplayName)
                ?? Validators.Email(request.Email)
                ?? Validators.Password(request.Password);
            if (problem is not null)
            {
                throw VibeException.Validation(problem);
            }

            var email = request.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email, ct))
            {
                // Clear next action, and no hint about whether the password was close.
                throw new VibeException(409, "An account already exists for this email. Sign in instead.");
            }

            var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            var user = new User
            {
                Id = Guid.NewGuid(),
                DisplayName = request.DisplayName.Trim(),
                Username = UsernameFrom(email),
                Email = email,
                PasswordHash = PasswordHasher.Hash(request.Password),
                CreatedAtEpochMillis = now,
                UpdatedAtEpochMillis = now,
            };

            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            return Results.Ok(await IssueAsync(user, db, jwt, clock, ct));
        })
        .WithSummary("Create an account")
        .Produces<AuthResponse>()
        .Produces<ErrorBody>(StatusCodes.Status409Conflict)
        .Produces<ErrorBody>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/login", async (
            LoginRequest request,
            VibeDbContext db,
            JwtIssuer jwt,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

            // Same message for "no such account" and "wrong password": no probing.
            if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
            {
                throw new VibeException(401, "Email or password is incorrect.");
            }

            return Results.Ok(await IssueAsync(user, db, jwt, clock, ct));
        })
        .WithSummary("Sign in with email and password")
        .Produces<AuthResponse>()
        .Produces<ErrorBody>(StatusCodes.Status401Unauthorized);

        group.MapPost("/google", async (
            GoogleAuthRequest request,
            VibeDbContext db,
            JwtIssuer jwt,
            GoogleTokenValidator google,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var identity = await google.ValidateAsync(request.IdToken, ct)
                ?? throw new VibeException(401, "That Google account could not be verified. Try again.");

            var email = identity.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

            if (user is null)
            {
                var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
                user = new User
                {
                    Id = Guid.NewGuid(),
                    DisplayName = identity.DisplayName,
                    Username = UsernameFrom(email),
                    Email = email,
                    // No password: the account can only be used through Google until a
                    // password is set from Settings.
                    PasswordHash = PasswordHasher.Hash(Domain.DecisionEngine.NewToken(32)),
                    PhotoUri = identity.PictureUri,
                    CreatedAtEpochMillis = now,
                    UpdatedAtEpochMillis = now,
                };
                db.Users.Add(user);
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(await IssueAsync(user, db, jwt, clock, ct));
        })
        .WithSummary("Exchange a Google id token for a VIBE session")
        .Produces<AuthResponse>()
        .Produces<ErrorBody>(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", async (
            RefreshRequest request,
            VibeDbContext db,
            JwtIssuer jwt,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var hash = Hash(request.RefreshToken);
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, ct);
            var now = clock.GetUtcNow().ToUnixTimeMilliseconds();

            if (stored is null || stored.Revoked || stored.ExpiresAtEpochMillis <= now)
            {
                throw new VibeException(401, "Your session expired. Sign in again.");
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, ct)
                ?? throw new VibeException(401, "Your session expired. Sign in again.");

            stored.Revoked = true; // rotate: a refresh token is single use
            return Results.Ok(await IssueAsync(user, db, jwt, clock, ct));
        })
        .WithSummary("Renew an access token")
        .Produces<AuthResponse>()
        .Produces<ErrorBody>(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (
            HttpContext context,
            VibeDbContext db,
            CancellationToken ct) =>
        {
            var userId = context.User.UserId();
            if (userId is not null)
            {
                await db.RefreshTokens
                    .Where(r => r.UserId == userId && !r.Revoked)
                    .ExecuteUpdateAsync(set => set.SetProperty(r => r.Revoked, true), ct);
            }

            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithSummary("Sign out and revoke refresh tokens");

        group.MapPost("/password-reset", async (PasswordResetRequest request, NotificationServiceStub mail) =>
        {
            // Always 204: whether the address exists is not something an anonymous
            // caller is allowed to learn. The real deployment mails a signed link.
            await mail.SendPasswordResetAsync(request.Email ?? string.Empty);
            return Results.NoContent();
        })
        .WithSummary("Request a password reset email")
        .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<AuthResponse> IssueAsync(
        User user,
        VibeDbContext db,
        JwtIssuer jwt,
        TimeProvider clock,
        CancellationToken ct)
    {
        var tokens = jwt.Issue(user);
        db.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = Hash(tokens.RefreshToken),
            UserId = user.Id,
            ExpiresAtEpochMillis = clock.GetUtcNow().Add(RefreshLifetime).ToUnixTimeMilliseconds(),
        });
        await db.SaveChangesAsync(ct);

        return new AuthResponse(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAtEpochMillis, user.ToDto());
    }

    private static string Hash(string? token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty))).ToLowerInvariant();

    private static string UsernameFrom(string email) =>
        new(
            (email.Split('@')[0] ?? "viber")
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .DefaultIfEmpty('v')
                .ToArray());
}

/// <summary>Mail is out of scope for the PoE; the sender is a seam for a real provider.</summary>
public class NotificationServiceStub(ILogger<NotificationServiceStub> logger)
{
    public Task SendPasswordResetAsync(string email)
    {
        logger.LogInformation("Password reset requested for {Email}", email);
        return Task.CompletedTask;
    }
}
