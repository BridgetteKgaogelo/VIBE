using Microsoft.EntityFrameworkCore;
using Vibe.Api.Domain;
using Vibe.Api.Security;

namespace Vibe.Api.Data;

/// <summary>
/// A development-only account, so the Swagger page and a freshly pointed app have
/// something to sign in with. Nothing is seeded outside Development.
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
        var owner = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "Lerato",
            Username = "lerato",
            Email = DemoEmail,
            PasswordHash = PasswordHasher.Hash(DemoPassword),
            Language = "en",
            ThemeMode = "DARK",
            CreatedAtEpochMillis = now,
            UpdatedAtEpochMillis = now,
        };

        var friends = new[]
        {
            new User { Id = Guid.NewGuid(), DisplayName = "Thandi", Username = "thandi", Email = "thandi@vibe.app", Language = "zu" },
            new User { Id = Guid.NewGuid(), DisplayName = "Sipho", Username = "sipho", Email = "sipho@vibe.app", Language = "en" },
            new User { Id = Guid.NewGuid(), DisplayName = "Naledi", Username = "naledi", Email = "naledi@vibe.app", Language = "st" },
        };

        foreach (var friend in friends)
        {
            friend.PasswordHash = PasswordHasher.Hash("Vibe2026go");
            friend.CreatedAtEpochMillis = now;
            friend.UpdatedAtEpochMillis = now;
        }

        db.Users.AddRange(friends);
        db.Users.Add(owner);

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Friday Night Crew",
            Icon = "🎉",
            InviteCode = DecisionEngine.GenerateInviteCode(),
            OwnerId = owner.Id,
            CreatedAtEpochMillis = now,
            UpdatedAtEpochMillis = now,
        };
        db.Groups.Add(group);

        db.GroupMembers.Add(new GroupMember
        {
            GroupId = group.Id,
            UserId = owner.Id,
            Role = "OWNER",
            JoinedAtEpochMillis = now,
        });

        foreach (var friend in friends)
        {
            db.GroupMembers.Add(new GroupMember
            {
                GroupId = group.Id,
                UserId = friend.Id,
                Role = "MEMBER",
                JoinedAtEpochMillis = now,
            });
        }

        var ideas = new (string Title, string Icon, Guid CreatedBy)[]
        {
            ("Go for pizza", "🍕", friends[0].Id),
            ("Bowling night", "🎳", friends[1].Id),
            ("Movie marathon", "🍿", friends[2].Id),
            ("Braai at the beach", "🏖️", owner.Id),
        };

        foreach (var (title, icon, createdBy) in ideas)
        {
            db.Activities.Add(new ActivityItem
            {
                Id = Guid.NewGuid(),
                GroupId = group.Id,
                Title = title,
                Icon = icon,
                Status = "SUGGESTED",
                CreatedBy = createdBy,
                CreatedAtEpochMillis = now,
                UpdatedAtEpochMillis = now,
            });
        }

        await db.SaveChangesAsync();
    }
}
