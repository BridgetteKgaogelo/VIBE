using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vibe.Api.Contracts;
using Vibe.Api.Data;
using Vibe.Api.Domain;
using Vibe.Api.Infrastructure;

namespace Vibe.Api.Services;

/// <summary>
/// Drains the client's offline queue.
/// </summary>
/// <remarks>
/// Every action is applied once (the <see cref="SyncReceipt"/> table is keyed by the
/// client id the app generates as <c>action:entity:queuedAt</c>), and the answer per
/// item is one of:
///
/// <list type="bullet">
/// <item><c>applied</c> - the server copy is now the change;</item>
/// <item><c>conflict</c> - the row moved on after the member queued the change; the
/// server sends its own copy back and the app asks the member which one to keep.
/// Nothing is overwritten silently;</item>
/// <item><c>rejected</c> - the change can never be applied (the round closed, the
/// row was deleted, validation failed) and the message says why.</item>
/// </list>
/// </remarks>
public class SyncService(
    VibeDbContext db,
    TimeProvider clock,
    NotificationService notifications)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SyncBatchResponse> ApplyAsync(Guid userId, SyncBatchRequest request)
    {
        var results = new List<SyncResultDto>(request.Items.Count);
        foreach (var item in request.Items)
        {
            results.Add(await ApplyOneAsync(userId, item));
            await db.SaveChangesAsync();
        }

        return new SyncBatchResponse(results);
    }

    private async Task<SyncResultDto> ApplyOneAsync(Guid userId, SyncItemRequest item)
    {
        // Retried batch: answer with what happened the first time.
        var receipt = await db.SyncReceipts.FirstOrDefaultAsync(r => r.ClientId == item.ClientId);
        if (receipt is not null)
        {
            return new SyncResultDto(receipt.ClientId, receipt.Status, receipt.ServerPayload, null);
        }

        var result = item.Action switch
        {
            "group.create" => await CreateGroupAsync(userId, item),
            "group.join" => await JoinGroupAsync(userId, item),
            "group.leave" => await LeaveGroupAsync(userId, item),
            "activity.add" => await AddActivityAsync(userId, item),
            "activity.update" => await UpdateActivityAsync(userId, item),
            "activity.delete" => await DeleteActivityAsync(userId, item),
            "decision.start" => await StartDecisionAsync(userId, item),
            "decision.vote" => await CastVoteAsync(userId, item),
            "decision.close" => await CloseDecisionAsync(userId, item),
            "plan.save" => await SavePlanAsync(userId, item),
            "plan.complete" or "memory.save" => await CompletePlanAsync(userId, item),
            "profile.update" => await UpdateProfileAsync(userId, item),
            _ => new SyncResultDto(item.ClientId, "rejected", null, $"Unknown action '{item.Action}'."),
        };

        db.SyncReceipts.Add(new SyncReceipt
        {
            ClientId = item.ClientId,
            UserId = userId,
            Status = result.Status,
            ServerPayload = result.ServerPayload,
            AppliedAtEpochMillis = Now(),
        });

        return result;
    }

    /* ------------------------------------------------------------- handlers */

    private async Task<SyncResultDto> CreateGroupAsync(Guid userId, SyncItemRequest item)
    {
        var body = Deserialize<CreateGroupRequest>(item);
        if (body is null)
        {
            return Rejected(item, "The queued group could not be read.");
        }

        if (!Guid.TryParse(item.EntityId, out var groupId))
        {
            return Rejected(item, "The queued group has no id.");
        }

        if (await db.Groups.AnyAsync(g => g.Id == groupId))
        {
            return Applied(item); // already created by an earlier attempt
        }

        var nameError = Security.Validators.GroupName(body.Name);
        if (nameError is not null)
        {
            return Rejected(item, nameError);
        }

        var now = Now();
        db.Groups.Add(new Group
        {
            Id = groupId,
            Name = body.Name.Trim(),
            Icon = body.Icon,
            InviteCode = await UniqueInviteCodeAsync(),
            OwnerId = userId,
            CreatedAtEpochMillis = now,
            UpdatedAtEpochMillis = now,
            ClientId = body.ClientId ?? item.ClientId,
        });
        db.GroupMembers.Add(new GroupMember
        {
            GroupId = groupId,
            UserId = userId,
            Role = "OWNER",
            JoinedAtEpochMillis = now,
        });

        return Applied(item);
    }

    private async Task<SyncResultDto> JoinGroupAsync(Guid userId, SyncItemRequest item)
    {
        var body = Deserialize<JoinGroupRequest>(item);
        if (body is null)
        {
            return Rejected(item, "The queued invite code could not be read.");
        }

        var code = Security.Validators.NormaliseInviteCode(body.InviteCode);
        var group = await db.Groups.FirstOrDefaultAsync(g => g.InviteCode == code);
        if (group is null)
        {
            return Rejected(item, "That invite code does not match a group.");
        }

        if (await db.GroupMembers.AnyAsync(m => m.GroupId == group.Id && m.UserId == userId))
        {
            return Applied(item);
        }

        db.GroupMembers.Add(new GroupMember
        {
            GroupId = group.Id,
            UserId = userId,
            Role = "MEMBER",
            JoinedAtEpochMillis = Now(),
        });

        await notifications.NotifyGroupAsync(
            group.Id,
            "INVITE",
            "A new member joined",
            $"{await DisplayNameAsync(userId)} joined {group.Name}.",
            userId);

        return Applied(item);
    }

    private async Task<SyncResultDto> LeaveGroupAsync(Guid userId, SyncItemRequest item)
    {
        var membership = await db.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == GroupId(item) && m.UserId == userId);

        if (membership is null)
        {
            return Applied(item);
        }

        if (membership.Role == "OWNER")
        {
            return Rejected(item, "Hand the group over before leaving it.");
        }

        db.GroupMembers.Remove(membership);
        return Applied(item);
    }

    private async Task<SyncResultDto> AddActivityAsync(Guid userId, SyncItemRequest item)
    {
        var body = Deserialize<UpsertActivityRequest>(item);
        if (body is null || !Guid.TryParse(item.EntityId, out var activityId))
        {
            return Rejected(item, "The queued idea could not be read.");
        }

        var groupId = ParseGroupId(body.GroupId);
        if (groupId is null || !await IsMemberAsync(groupId.Value, userId))
        {
            return Rejected(item, "You are not a member of that group any more.");
        }

        if (await db.Activities.AnyAsync(a => a.Id == activityId))
        {
            return Applied(item);
        }

        var titleError = Security.Validators.ActivityTitle(body.Title);
        if (titleError is not null)
        {
            return Rejected(item, titleError);
        }

        var now = Now();
        db.Activities.Add(new ActivityItem
        {
            Id = activityId,
            GroupId = groupId.Value,
            Title = body.Title.Trim(),
            Description = body.Description,
            Icon = body.Icon,
            Status = "SUGGESTED",
            CreatedBy = userId,
            Favourite = body.Favourite ?? false,
            CreatedAtEpochMillis = now,
            UpdatedAtEpochMillis = now,
            ClientId = body.ClientId ?? item.ClientId,
        });

        await notifications.NotifyGroupAsync(
            groupId.Value,
            "NEW_ACTIVITY",
            "New idea on the Vibe List",
            $"{await DisplayNameAsync(userId)} added {body.Title.Trim()}.",
            userId);

        return Applied(item);
    }

    private async Task<SyncResultDto> UpdateActivityAsync(Guid userId, SyncItemRequest item)
    {
        var body = Deserialize<UpsertActivityRequest>(item);
        if (body is null || !Guid.TryParse(item.EntityId, out var activityId))
        {
            return Rejected(item, "The queued change could not be read.");
        }

        var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == activityId);
        if (activity is null)
        {
            return Rejected(item, "That idea was deleted.");
        }

        if (!await IsMemberAsync(activity.GroupId, userId))
        {
            return Rejected(item, "You are not a member of that group any more.");
        }

        if (activity.UpdatedAtEpochMillis > item.QueuedAtEpochMillis)
        {
            // Somebody changed it while the member was offline: report, do not overwrite.
            return Conflict(item, Json.Serialize(activity));
        }

        activity.Title = body.Title.Trim();
        activity.Description = body.Description;
        activity.Icon = body.Icon;
        activity.Favourite = body.Favourite ?? activity.Favourite;
        activity.Status = body.Status ?? activity.Status;
        activity.UpdatedAtEpochMillis = Now();

        return Applied(item);
    }

    private async Task<SyncResultDto> DeleteActivityAsync(Guid userId, SyncItemRequest item)
    {
        if (!Guid.TryParse(item.EntityId, out var activityId))
        {
            return Rejected(item, "The queued delete could not be read.");
        }

        var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == activityId);
        if (activity is null)
        {
            return Applied(item);
        }

        if (!await IsMemberAsync(activity.GroupId, userId))
        {
            return Rejected(item, "You are not a member of that group any more.");
        }

        if (activity.UpdatedAtEpochMillis > item.QueuedAtEpochMillis)
        {
            return Conflict(item, Json.Serialize(activity));
        }

        db.Activities.Remove(activity);
        return Applied(item);
    }

    private async Task<SyncResultDto> StartDecisionAsync(Guid userId, SyncItemRequest item)
    {
        var body = Deserialize<StartDecisionRequest>(item);
        if (body is null)
        {
            return Rejected(item, "The queued round could not be read.");
        }

        var groupId = ParseGroupId(body.GroupId) ?? ParseGroupId(item.EntityId.Split(':').FirstOrDefault());
        if (groupId is null)
        {
            return Rejected(item, "The queued round has no group.");
        }

        var rounds = new DecisionService(db, clock, notifications);
        try
        {
            await rounds.StartRoundAsync(groupId.Value, userId, body with { ClientId = item.ClientId });
            return Applied(item);
        }
        catch (VibeException error)
        {
            // Already open, not the owner, not enough ideas: the member sees why.
            return Rejected(item, error.Message);
        }
    }

    private async Task<SyncResultDto> CastVoteAsync(Guid userId, SyncItemRequest item)
    {
        var body = Deserialize<CastVoteRequest>(item);
        if (body is null)
        {
            return Rejected(item, "The queued vote could not be read.");
        }

        var parts = item.EntityId.Split(':');
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var decisionId))
        {
            return Rejected(item, "The queued vote has no round.");
        }

        var decision = await db.Decisions.FirstOrDefaultAsync(d => d.Id == decisionId);
        if (decision is null)
        {
            return Rejected(item, "That round no longer exists.");
        }

        if (decision.State != "OPEN")
        {
            // The round closed while the member was offline: the vote cannot count,
            // and the app already shows the closed round when it refreshes.
            return Rejected(item, "The round closed before your vote arrived.");
        }

        if (!await IsMemberAsync(decision.GroupId, userId))
        {
            return Rejected(item, "You are not a member of that group any more.");
        }

        await new DecisionService(db, clock, notifications).CastVoteAsync(
            decisionId,
            userId,
            body with { ClientTimestampEpochMillis = item.QueuedAtEpochMillis });

        return Applied(item);
    }

    private async Task<SyncResultDto> CloseDecisionAsync(Guid userId, SyncItemRequest item)
    {
        if (!Guid.TryParse(item.EntityId, out var decisionId))
        {
            return Rejected(item, "The queued close has no round.");
        }

        await new DecisionService(db, clock, notifications).CloseAsync(decisionId, userId);
        return Applied(item);
    }

    private async Task<SyncResultDto> SavePlanAsync(Guid userId, SyncItemRequest item)
    {
        var body = Deserialize<SavePlanRequest>(item);
        if (body is null || !Guid.TryParse(item.EntityId, out var planId))
        {
            return Rejected(item, "The queued plan could not be read.");
        }

        if (!await IsMemberAsync(body.GroupId, userId))
        {
            return Rejected(item, "You are not a member of that group any more.");
        }

        var existing = await db.Plans.FirstOrDefaultAsync(p => p.Id == planId);
        if (existing is not null && existing.UpdatedAtEpochMillis > item.QueuedAtEpochMillis)
        {
            return Conflict(item, Json.Serialize(existing));
        }

        if (existing is null)
        {
            db.Plans.Add(new Plan
            {
                Id = planId,
                GroupId = body.GroupId,
                ActivityId = body.ActivityId,
                ScheduledAtEpochMillis = body.ScheduledAtEpochMillis,
                Completed = false,
                CreatedBy = userId,
                UpdatedAtEpochMillis = Now(),
                ClientId = item.ClientId,
            });
        }
        else
        {
            existing.ScheduledAtEpochMillis = body.ScheduledAtEpochMillis;
            existing.UpdatedAtEpochMillis = Now();
        }

        return Applied(item);
    }

    private async Task<SyncResultDto> CompletePlanAsync(Guid userId, SyncItemRequest item)
    {
        var body = Deserialize<CompletePlanRequest>(item);
        if (body is null)
        {
            return Rejected(item, "The queued memory could not be read.");
        }

        var ratingError = Security.Validators.Rating(body.Rating);
        if (ratingError is not null)
        {
            return Rejected(item, ratingError);
        }

        var captionError = Security.Validators.Caption(body.Caption);
        if (captionError is not null)
        {
            return Rejected(item, captionError);
        }

        var planId = body.PlanId is { } raw && Guid.TryParse(raw, out var parsed)
            ? parsed
            : Guid.TryParse(item.EntityId, out var entityPlan) ? entityPlan : (Guid?)null;

        var plan = planId is null ? null : await db.Plans.FirstOrDefaultAsync(p => p.Id == planId);
        if (plan is null)
        {
            return Rejected(item, "That plan was removed.");
        }

        if (!await IsMemberAsync(plan.GroupId, userId))
        {
            return Rejected(item, "You are not a member of that group any more.");
        }

        var now = Now();
        plan.Completed = true;
        plan.UpdatedAtEpochMillis = now;

        var memory = item.Action == "memory.save" && Guid.TryParse(item.EntityId, out var memoryId)
            ? await db.Memories.FirstOrDefaultAsync(m => m.Id == memoryId)
            : await db.Memories.FirstOrDefaultAsync(m => m.PlanId == plan.Id);

        var photos = Mapping.EncodeList(body.PhotoUris ?? []);
        if (memory is null)
        {
            memory = new Memory
            {
                Id = Guid.TryParse(item.EntityId, out var newId) ? newId : Guid.NewGuid(),
                GroupId = plan.GroupId,
                PlanId = plan.Id,
                ActivityId = plan.ActivityId,
                CompletedAtEpochMillis = item.QueuedAtEpochMillis > 0 ? item.QueuedAtEpochMillis : now,
                Caption = body.Caption,
                Rating = body.Rating,
                PhotoUrlsCsv = photos,
                CreatedBy = userId,
                UpdatedAtEpochMillis = now,
                ClientId = item.ClientId,
            };
            db.Memories.Add(memory);
        }
        else if (memory.UpdatedAtEpochMillis > item.QueuedAtEpochMillis)
        {
            return Conflict(item, Json.Serialize(memory));
        }
        else
        {
            memory.Caption = body.Caption;
            memory.Rating = body.Rating;
            memory.PhotoUrlsCsv = photos.Length == 0 ? memory.PhotoUrlsCsv : photos;
            memory.UpdatedAtEpochMillis = now;
        }

        await db.Activities
            .Where(a => a.Id == plan.ActivityId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.Status, "COMPLETED")
                .SetProperty(a => a.UpdatedAtEpochMillis, now));

        await notifications.NotifyGroupAsync(
            plan.GroupId,
            "MEMORY",
            "A plan became a memory",
            "Photos, a caption and the group rating are waiting.",
            userId);

        return Applied(item);
    }

    private async Task<SyncResultDto> UpdateProfileAsync(Guid userId, SyncItemRequest item)
    {
        var body = Deserialize<UpdateProfileRequest>(item);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (body is null || user is null)
        {
            return Rejected(item, "The queued profile change could not be read.");
        }

        if (user.UpdatedAtEpochMillis > item.QueuedAtEpochMillis)
        {
            return Conflict(item, Json.Serialize(user.ToDto()));
        }

        if (body.DisplayName is { } name)
        {
            var nameError = Security.Validators.DisplayName(name);
            if (nameError is not null)
            {
                return Rejected(item, nameError);
            }

            user.DisplayName = name.Trim();
        }

        user.Username = body.Username ?? user.Username;
        user.Language = body.Language ?? user.Language;
        user.ThemeMode = body.ThemeMode ?? user.ThemeMode;
        user.NotificationsEnabled = body.NotificationsEnabled ?? user.NotificationsEnabled;
        user.PrivacyMembersOnly = body.PrivacyMembersOnly ?? user.PrivacyMembersOnly;
        user.PhotoUri = body.PhotoUri ?? user.PhotoUri;
        user.UpdatedAtEpochMillis = Now();

        return Applied(item);
    }

    /* -------------------------------------------------------------- helpers */

    private static T? Deserialize<T>(SyncItemRequest item)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(item.PayloadJson, Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static SyncResultDto Applied(SyncItemRequest item) => new(item.ClientId, "applied", null, null);

    private static SyncResultDto Rejected(SyncItemRequest item, string message) =>
        new(item.ClientId, "rejected", null, message);

    private static SyncResultDto Conflict(SyncItemRequest item, string serverPayload) =>
        new(item.ClientId, "conflict", serverPayload, "This changed on the server while you were offline.");

    private static Guid? ParseGroupId(string? raw) =>
        Guid.TryParse(raw, out var id) ? id : null;

    private Guid GroupId(SyncItemRequest item) =>
        ParseGroupId(item.EntityId) ?? throw VibeException.Validation("The queued change has no group.");

    private Task<bool> IsMemberAsync(Guid groupId, Guid userId) =>
        db.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == userId);

    private async Task<string> DisplayNameAsync(Guid userId) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync() ?? "Member";

    private async Task<string> UniqueInviteCodeAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = DecisionEngine.GenerateInviteCode();
            if (!await db.Groups.AnyAsync(g => g.InviteCode == code))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique invite code.");
    }

    private long Now() => clock.GetUtcNow().ToUnixTimeMilliseconds();
}
