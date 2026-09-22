using Microsoft.EntityFrameworkCore;
using Vibe.Api.Domain;
using Vibe.Api.Security;

namespace Vibe.Api.Data;

/// <summary>
/// Development-only member accounts, so the Swagger page and a freshly pointed app have
/// something to sign in with.
///
/// No groups, ideas, plans or memories are seeded. The app starts empty on purpose, so
/// what a reviewer sees is exactly what they created: create a group, add ideas, run a
/// round. Nothing is seeded outside Development.
/// </summary>
public static class DevSeed
{
    public const string DemoEmail = "lerato@vibe.app";
    public const string DemoPassword = "Vibe2026go";

    public static async Task EnsureDemoAccountAsync(VibeDbContext db, TimeProvider clock)
    {
        if (await db.Users.AnyAsync(u => u.Email == DemoEmail))
        {
            return;
        }

        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var passwordHash = PasswordHasher.Hash(DemoPassword);

        var members = new (string DisplayName, string Username, string Email, string Language)[]
        {
            ("Lerato", "lerato", DemoEmail, "en"),
            ("Thandi", "thandi", "thandi@vibe.app", "zu"),
            ("Sipho", "sipho", "sipho@vibe.app", "en"),
            ("Naledi", "naledi", "naledi@vibe.app", "st"),
        };

        foreach (var (displayName, username, email, language) in members)
        {
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                DisplayName = displayName,
                Username = username,
                Email = email,
                PasswordHash = passwordHash,
                Language = language,
                ThemeMode = "DARK",
                CreatedAtEpochMillis = now,
                UpdatedAtEpochMillis = now,
            });
        }

        await db.SaveChangesAsync();
    }
}
