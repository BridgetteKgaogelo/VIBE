using Microsoft.EntityFrameworkCore;

namespace Vibe.Api.Data;

/* ------------------------------------------------------------------ entities */

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>Salted one-way hash - never the password itself.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string Language { get; set; } = "en";
    public string ThemeMode { get; set; } = "DARK";
    public bool NotificationsEnabled { get; set; } = true;
    public bool PrivacyMembersOnly { get; set; } = true;
    public string? PhotoUri { get; set; }
    public long CreatedAtEpochMillis { get; set; }
    public long UpdatedAtEpochMillis { get; set; }

    public List<GroupMember> Memberships { get; set; } = [];
}

public class Group
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "🎉";
    public string InviteCode { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public long CreatedAtEpochMillis { get; set; }
    public long UpdatedAtEpochMillis { get; set; }

    /// <summary>Idempotency key from the client, so a queued create is never duplicated.</summary>
    public string? ClientId { get; set; }

    public List<GroupMember> Members { get; set; } = [];
    public List<ActivityItem> Activities { get; set; } = [];
}

public class GroupMember
{
    public Guid GroupId { get; set; }
    public Group? Group { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>OWNER or MEMBER.</summary>
    public string Role { get; set; } = "MEMBER";

    public long JoinedAtEpochMillis { get; set; }
}

public class ActivityItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "🎉";

    /// <summary>SUGGESTED | ACTIVE | ELIMINATED | WINNER | COMPLETED.</summary>
    public string Status { get; set; } = "SUGGESTED";

    public Guid CreatedBy { get; set; }
    public bool Favourite { get; set; }
    public int YesVotes { get; set; }
    public int ParticipantCount { get; set; }
    public long CreatedAtEpochMillis { get; set; }
    public long UpdatedAtEpochMillis { get; set; }
    public string? ClientId { get; set; }
}

public class Decision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public int RoundNumber { get; set; } = 1;

    /// <summary>OPEN | CLOSED | SETTLED.</summary>
    public string State { get; set; } = "OPEN";

    public long? DeadlineEpochMillis { get; set; }
    public Guid? WinnerActivityId { get; set; }
    public Guid StartedBy { get; set; }
    public long StartedAtEpochMillis { get; set; }
    public long UpdatedAtEpochMillis { get; set; }
    public string? ClientId { get; set; }

    /// <summary>The options of this round, comma separated - the round's own snapshot.</summary>
    public string RoundActivityIdsCsv { get; set; } = string.Empty;

    public List<Vote> Votes { get; set; } = [];
}

public class Vote
{
    public Guid DecisionId { get; set; }
    public Decision? Decision { get; set; }
    public Guid ActivityId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>true = YES, false = NO.</summary>
    public bool Choice { get; set; }

    public long CastAtEpochMillis { get; set; }
}

public class Plan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public Guid ActivityId { get; set; }
    public long? ScheduledAtEpochMillis { get; set; }
    public bool Completed { get; set; }
    public Guid CreatedBy { get; set; }
    public long UpdatedAtEpochMillis { get; set; }
    public string? ClientId { get; set; }
}

public class Memory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public Guid PlanId { get; set; }
    public Guid ActivityId { get; set; }
    public long CompletedAtEpochMillis { get; set; }
    public string Caption { get; set; } = string.Empty;
    public int Rating { get; set; }

    /// <summary>Photo URLs, comma separated (blob storage URLs in a deployment).</summary>
    public string PhotoUrlsCsv { get; set; } = string.Empty;

    public Guid CreatedBy { get; set; }
    public long UpdatedAtEpochMillis { get; set; }
    public string? ClientId { get; set; }
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>INVITE | NEW_ACTIVITY | DEADLINE | WINNER | MEMORY.</summary>
    public string Type { get; set; } = "NEW_ACTIVITY";

    public bool Read { get; set; }
    public long CreatedAtEpochMillis { get; set; }
    public Guid? GroupId { get; set; }
}

public class DeviceToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public long CreatedAtEpochMillis { get; set; }
}

/// <summary>
/// A refresh token. Only the hash is stored, so a database leak cannot be replayed,
/// and the row is rotated every time it is used.
/// </summary>
public class RefreshToken
{
    public string TokenHash { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public long ExpiresAtEpochMillis { get; set; }
    public bool Revoked { get; set; }
}

/// <summary>Applied sync actions, so a retried batch is never applied twice.</summary>
public class SyncReceipt
{
    public string ClientId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Status { get; set; } = "applied";
    public string? ServerPayload { get; set; }
    public long AppliedAtEpochMillis { get; set; }
}

/* ------------------------------------------------------------------ context */

public class VibeDbContext(DbContextOptions<VibeDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<ActivityItem> Activities => Set<ActivityItem>();
    public DbSet<Decision> Decisions => Set<Decision>();
    public DbSet<Vote> Votes => Set<Vote>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Memory> Memories => Set<Memory>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SyncReceipt> SyncReceipts => Set<SyncReceipt>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(254);
            e.Property(u => u.DisplayName).HasMaxLength(ValidatorsMax);
        });

        builder.Entity<Group>(e =>
        {
            e.HasIndex(g => g.InviteCode).IsUnique();
            e.HasIndex(g => g.ClientId);
            e.Property(g => g.InviteCode).HasMaxLength(8);
            e.Property(g => g.Name).HasMaxLength(60);
        });

        builder.Entity<GroupMember>(e =>
        {
            e.HasKey(m => new { m.GroupId, m.UserId });
            e.HasOne(m => m.Group).WithMany(g => g.Members).HasForeignKey(m => m.GroupId);
            e.HasOne(m => m.User).WithMany(u => u.Memberships).HasForeignKey(m => m.UserId);
        });

        builder.Entity<ActivityItem>(e =>
        {
            e.HasIndex(a => a.GroupId);
            e.HasIndex(a => a.ClientId);
            e.Property(a => a.Title).HasMaxLength(80);
        });

        builder.Entity<Decision>(e =>
        {
            e.HasIndex(d => new { d.GroupId, d.State });
            e.HasIndex(d => d.ClientId);
        });

        // One YES/NO vote per member per activity: the composite key enforces it in
        // the database, not just in code.
        builder.Entity<Vote>(e =>
        {
            e.HasKey(v => new { v.DecisionId, v.ActivityId, v.UserId });
            e.HasOne(v => v.Decision).WithMany(d => d.Votes).HasForeignKey(v => v.DecisionId);
        });

        builder.Entity<Plan>(e => e.HasIndex(p => p.GroupId));
        builder.Entity<Memory>(e => e.HasIndex(m => m.GroupId));
        builder.Entity<Notification>(e => e.HasIndex(n => new { n.UserId, n.Read }));
        builder.Entity<DeviceToken>(e => e.HasIndex(d => d.Token).IsUnique());
        builder.Entity<RefreshToken>(e =>
        {
            e.HasKey(r => r.TokenHash);
            e.HasIndex(r => r.UserId);
        });

        builder.Entity<SyncReceipt>(e => e.HasKey(r => r.ClientId));
    }

    private const int ValidatorsMax = 40;
}
