using Microsoft.EntityFrameworkCore;
using Vibe.Api.Contracts;
using Vibe.Api.Data;
using Vibe.Api.Domain;
using Vibe.Api.Infrastructure;
using Vibe.Api.Security;

namespace Vibe.Api.Endpoints;

public static class GroupEndpoints
{
    public static void MapGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var groups = app.MapGroup("/api/v1/groups")
            .RequireAuthorization()
            .WithTags("Groups and the Vibe List");

        groups.MapGet("", async (HttpContext context, VibeDbContext db, CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var mine = await db.GroupMembers
                .Where(m => m.UserId == userId)
                .Select(m => m.GroupId)
                .ToListAsync(ct);

            var rows = await db.Groups
                .Include(g => g.Members).ThenInclude(m => m.User)
                .Where(g => mine.Contains(g.Id))
                .OrderBy(g => g.Name)
                .ToListAsync(ct);

            var counts = await db.Activities
                .Where(a => mine.Contains(a.GroupId))
                .GroupBy(a => a.GroupId)
                .Select(entry => new { entry.Key, Count = entry.Count() })
                .ToDictionaryAsync(entry => entry.Key, entry => entry.Count, ct);

            return Results.Ok(rows.Select(g => g.ToDto(userId, counts.GetValueOrDefault(g.Id))).ToList());
        })
        .WithSummary("The member's groups");

        groups.MapGet("/{groupId:guid}", async (
            Guid groupId,
            HttpContext context,
            VibeDbContext db,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var group = await LoadAsync(db, groupId, ct);
            RequireMember(group, userId);

            var activityCount = await db.Activities.CountAsync(a => a.GroupId == groupId, ct);
            return Results.Ok(group.ToDto(userId, activityCount));
        })
        .WithSummary("One group with its members")
        .Produces<ErrorBody>(StatusCodes.Status403Forbidden);

        groups.MapPost("", async (
            CreateGroupRequest request,
            HttpContext context,
            VibeDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var problem = Validators.GroupName(request.Name);
            if (problem is not null)
            {
                throw VibeException.Validation(problem, nameof(request.Name));
            }

            var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            var group = new Group
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Icon = request.Icon,
                InviteCode = await UniqueCodeAsync(db, ct),
                OwnerId = userId,
                CreatedAtEpochMillis = now,
                UpdatedAtEpochMillis = now,
                ClientId = request.ClientId,
            };

            db.Groups.Add(group);
            db.GroupMembers.Add(new GroupMember
            {
                GroupId = group.Id,
                UserId = userId,
                Role = "OWNER",
                JoinedAtEpochMillis = now,
            });
            await db.SaveChangesAsync(ct);

            var fresh = await LoadAsync(db, group.Id, ct);
            return Results.Ok(fresh.ToDto(userId, 0));
        })
        .WithSummary("Create a group");

        groups.MapPost("/join", async (
            JoinGroupRequest request,
            HttpContext context,
            VibeDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            if (!Validators.IsInviteCode(request.InviteCode))
            {
                throw VibeException.Validation("That invite code does not look right - codes are six characters.", nameof(request.InviteCode));
            }

            var code = Validators.NormaliseInviteCode(request.InviteCode);
            var group = await db.Groups.FirstOrDefaultAsync(g => g.InviteCode == code, ct)
                ?? throw VibeException.NotFound("No group uses that invite code. Check it with the owner.");

            if (!await db.GroupMembers.AnyAsync(m => m.GroupId == group.Id && m.UserId == userId, ct))
            {
                var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
                db.GroupMembers.Add(new GroupMember
                {
                    GroupId = group.Id,
                    UserId = userId,
                    Role = "MEMBER",
                    JoinedAtEpochMillis = now,
                });
                await db.SaveChangesAsync(ct);
            }

            var fresh = await LoadAsync(db, group.Id, ct);
            var activityCount = await db.Activities.CountAsync(a => a.GroupId == group.Id, ct);
            return Results.Ok(fresh.ToDto(userId, activityCount));
        })
        .WithSummary("Join a group with an invite code")
        .Produces<ErrorBody>(StatusCodes.Status404NotFound);

        groups.MapPost("/{groupId:guid}/leave", async (
            Guid groupId,
            HttpContext context,
            VibeDbContext db,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var membership = await db.GroupMembers
                .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, ct)
                ?? throw VibeException.NotFound("You are not in that group.");

            if (membership.Role == "OWNER")
            {
                throw VibeException.Validation("Hand the group over to another member before leaving it.");
            }

            db.GroupMembers.Remove(membership);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithSummary("Leave a group");

        groups.MapDelete("/{groupId:guid}/members/{userId:guid}", async (
            Guid groupId,
            Guid userId,
            HttpContext context,
            VibeDbContext db,
            CancellationToken ct) =>
        {
            var caller = context.User.RequireUserId();
            var group = await LoadAsync(db, groupId, ct);
            if (group.OwnerId != caller)
            {
                throw VibeException.Forbidden("Only the group owner can remove members.");
            }

            if (userId == caller)
            {
                throw VibeException.Validation("The owner cannot remove themselves - hand the group over instead.");
            }

            var membership = await db.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, ct)
                ?? throw VibeException.NotFound("That member is not in the group.");

            db.GroupMembers.Remove(membership);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithSummary("Remove a member (owner only)");

        groups.MapGet("/{groupId:guid}/activities", async (
            Guid groupId,
            HttpContext context,
            VibeDbContext db,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var group = await LoadAsync(db, groupId, ct);
            RequireMember(group, userId);
            return Results.Ok(await ActivitiesAsync(db, groupId, ct));
        })
        .WithSummary("The group's Vibe List");

        groups.MapPost("/{groupId:guid}/activities", async (
            Guid groupId,
            UpsertActivityRequest request,
            HttpContext context,
            VibeDbContext db,
            TimeProvider clock,
            NotificationService notifications,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var group = await LoadAsync(db, groupId, ct);
            RequireMember(group, userId);

            var problem = Validators.ActivityTitle(request.Title);
            if (problem is not null)
            {
                throw VibeException.Validation(problem, nameof(request.Title));
            }

            var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            var activity = new ActivityItem
            {
                Id = Guid.TryParse(request.ClientId, out var clientId) ? clientId : Guid.NewGuid(),
                GroupId = groupId,
                Title = request.Title.Trim(),
                Description = request.Description,
                Icon = request.Icon,
                Status = "SUGGESTED",
                CreatedBy = userId,
                Favourite = request.Favourite ?? false,
                CreatedAtEpochMillis = now,
                UpdatedAtEpochMillis = now,
                ClientId = request.ClientId,
            };
            db.Activities.Add(activity);
            await db.SaveChangesAsync(ct);

            await notifications.NotifyGroupAsync(
                groupId,
                "NEW_ACTIVITY",
                "New idea on the Vibe List",
                $"{await DisplayNameAsync(db, userId, ct)} added {activity.Title}.",
                userId,
                ct);

            return Results.Ok(activity.ToDto(await DisplayNameAsync(db, userId, ct)));
        })
        .WithSummary("Add an idea to the Vibe List");

        var activities = app.MapGroup("/api/v1/activities").RequireAuthorization().WithTags("Vibe List");

        activities.MapPatch("/{activityId:guid}", async (
            Guid activityId,
            UpsertActivityRequest request,
            HttpContext context,
            VibeDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == activityId, ct)
                ?? throw VibeException.NotFound("That idea no longer exists.");

            var group = await LoadAsync(db, activity.GroupId, ct);
            RequireMember(group, userId);

            activity.Title = request.Title.Trim();
            activity.Description = request.Description;
            activity.Icon = request.Icon;
            activity.Favourite = request.Favourite ?? activity.Favourite;
            activity.Status = request.Status ?? activity.Status;
            activity.UpdatedAtEpochMillis = clock.GetUtcNow().ToUnixTimeMilliseconds();
            await db.SaveChangesAsync(ct);

            return Results.Ok(activity.ToDto(await DisplayNameAsync(db, activity.CreatedBy, ct)));
        })
        .WithSummary("Edit an idea");

        activities.MapDelete("/{activityId:guid}", async (
            Guid activityId,
            HttpContext context,
            VibeDbContext db,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == activityId, ct)
                ?? throw VibeException.NotFound("That idea no longer exists.");

            var group = await LoadAsync(db, activity.GroupId, ct);
            RequireMember(group, userId);

            db.Activities.Remove(activity);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithSummary("Delete an idea");

        app.MapGet("/api/v1/users/me/activities", async (
            HttpContext context,
            VibeDbContext db,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var groupIds = await db.GroupMembers
                .Where(m => m.UserId == userId)
                .Select(m => m.GroupId)
                .ToListAsync(ct);

            var rows = await db.Activities
                .Where(a => a.GroupId == groupIds.Contains(a.GroupId) || a.CreatedBy == userId)
                .OrderByDescending(a => a.CreatedAtEpochMillis)
                .ToListAsync(ct);

            return Results.Ok(await ToDtosAsync(db, rows, ct));
        })
        .RequireAuthorization()
        .WithTags("Vibe List")
        .WithSummary("Every idea from the member's groups");
    }

    /* ---------------------------------------------------------------- helpers */

    internal static async Task<Group> LoadAsync(VibeDbContext db, Guid groupId, CancellationToken ct) =>
        await db.Groups
            .Include(g => g.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(g => g.Id == groupId, ct)
        ?? throw VibeException.NotFound("That group no longer exists.");

    internal static void RequireMember(Group group, Guid userId)
    {
        if (group.Members.All(m => m.UserId != userId))
        {
            throw VibeException.Forbidden("You are not a member of this group.");
        }
    }

    internal static async Task<List<ActivityDto>> ActivitiesAsync(VibeDbContext db, Guid groupId, CancellationToken ct) =>
        await ToDtosAsync(
            db,
            await db.Activities.Where(a => a.GroupId == groupId).OrderByDescending(a => a.CreatedAtEpochMillis).ToListAsync(ct),
            ct);

    internal static async Task<List<ActivityDto>> ToDtosAsync(VibeDbContext db, List<ActivityItem> rows, CancellationToken ct)
    {
        var names = await db.Users
            .Where(u => rows.Select(r => r.CreatedBy).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return rows.Select(r => r.ToDto(names.GetValueOrDefault(r.CreatedBy, "Member"))).ToList();
    }

    private static async Task<string> DisplayNameAsync(VibeDbContext db, Guid userId, CancellationToken ct) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "Member";

    private static async Task<string> UniqueCodeAsync(VibeDbContext db, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = DecisionEngine.GenerateInviteCode();
            if (!await db.Groups.AnyAsync(g => g.InviteCode == code, ct))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique invite code.");
    }
}
