using Microsoft.EntityFrameworkCore;
using Vibe.Api.Contracts;
using Vibe.Api.Data;
using Vibe.Api.Infrastructure;
using Vibe.Api.Services;

namespace Vibe.Api.Endpoints;

public static class DecisionEndpoints
{
    public static void MapDecisionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/groups/{groupId:guid}/decisions", async (
            Guid groupId,
            StartDecisionRequest request,
            HttpContext context,
            DecisionService decisions,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var dto = await decisions.StartRoundAsync(groupId, userId, request);
            return Results.Ok(dto);
        })
        .RequireAuthorization()
        .WithTags("Decide For Us")
        .WithSummary("Start a round (owner only)")
        .Produces<DecisionDto>()
        .Produces<ErrorBody>(StatusCodes.Status403Forbidden)
        .Produces<ErrorBody>(StatusCodes.Status422UnprocessableEntity);

        var decisionGroup = app.MapGroup("/api/v1/decisions").RequireAuthorization().WithTags("Decide For Us");

        decisionGroup.MapGet("/{decisionId:guid}", async (
            Guid decisionId,
            HttpContext context,
            DecisionService decisions,
            CancellationToken ct) =>
            Results.Ok(await decisions.GetAsync(decisionId, context.User.RequireUserId())))
        .WithSummary("The current state of a round");

        decisionGroup.MapPost("/{decisionId:guid}/votes", async (
            Guid decisionId,
            CastVoteRequest request,
            HttpContext context,
            DecisionService decisions,
            CancellationToken ct) =>
            Results.Ok(await decisions.CastVoteAsync(decisionId, context.User.RequireUserId(), request)))
        .WithSummary("Cast one YES/NO vote")
        .Produces<VoteAckDto>()
        .Produces<ErrorBody>(StatusCodes.Status409Conflict);

        decisionGroup.MapPost("/{decisionId:guid}/close", async (
            Guid decisionId,
            HttpContext context,
            DecisionService decisions,
            CancellationToken ct) =>
            Results.Ok(await decisions.CloseAsync(decisionId, context.User.RequireUserId())))
        .WithSummary("Close the round and move the game on");

        decisionGroup.MapPost("/{decisionId:guid}/surprise", async (
            Guid decisionId,
            SurpriseRequest request,
            HttpContext context,
            DecisionService decisions,
            CancellationToken ct) =>
            Results.Ok(await decisions.SurpriseAsync(decisionId, context.User.RequireUserId(), request)))
        .WithSummary("\"Surprise Me\": a random eligible idea");
    }

    public static void MapSupportEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/v1/users/me").RequireAuthorization().WithTags("Account");

        users.MapGet("", async (HttpContext context, VibeDbContext db, CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                ?? throw VibeException.NotFound("That account no longer exists.");
            return Results.Ok(user.ToDto());
        })
        .WithSummary("The signed-in member");

        users.MapPatch("", async (
            UpdateProfileRequest request,
            HttpContext context,
            VibeDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                ?? throw VibeException.NotFound("That account no longer exists.");

            if (request.DisplayName is { } name)
            {
                var problem = Vibe.Api.Security.Validators.DisplayName(name);
                if (problem is not null)
                {
                    throw VibeException.Validation(problem, nameof(request.DisplayName));
                }

                user.DisplayName = name.Trim();
            }

            user.Username = request.Username ?? user.Username;
            user.Language = request.Language ?? user.Language;
            user.ThemeMode = request.ThemeMode ?? user.ThemeMode;
            user.NotificationsEnabled = request.NotificationsEnabled ?? user.NotificationsEnabled;
            user.PrivacyMembersOnly = request.PrivacyMembersOnly ?? user.PrivacyMembersOnly;
            user.PhotoUri = request.PhotoUri ?? user.PhotoUri;
            user.UpdatedAtEpochMillis = clock.GetUtcNow().ToUnixTimeMilliseconds();
            await db.SaveChangesAsync(ct);

            return Results.Ok(user.ToDto());
        })
        .WithSummary("Edit the profile or the settings");

        users.MapPost("/password", async (
            ChangePasswordRequest request,
            HttpContext context,
            VibeDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                ?? throw VibeException.NotFound("That account no longer exists.");

            if (!Vibe.Api.Security.PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            {
                throw VibeException.Validation("Your current password is incorrect.", nameof(request.CurrentPassword));
            }

            var problem = Vibe.Api.Security.Validators.Password(request.NewPassword);
            if (problem is not null)
            {
                throw VibeException.Validation(problem, nameof(request.NewPassword));
            }

            user.PasswordHash = Vibe.Api.Security.PasswordHasher.Hash(request.NewPassword);
            user.UpdatedAtEpochMillis = clock.GetUtcNow().ToUnixTimeMilliseconds();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithSummary("Change the password");

        app.MapGet("/api/v1/groups/{groupId:guid}/plans", async (
            Guid groupId,
            HttpContext context,
            VibeDbContext db,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var group = await GroupEndpoints.LoadAsync(db, groupId, ct);
            GroupEndpoints.RequireMember(group, userId);

            var plans = await db.Plans.Where(p => p.GroupId == groupId).ToListAsync(ct);
            return Results.Ok(await PlansToDtosAsync(db, plans, ct));
        })
        .RequireAuthorization()
        .WithTags("Plans and memories")
        .WithSummary("The group's plans");

        app.MapPost("/api/v1/plans", async (
            SavePlanRequest request,
            HttpContext context,
            VibeDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var group = await GroupEndpoints.LoadAsync(db, request.GroupId, ct);
            GroupEndpoints.RequireMember(group, userId);

            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == request.ActivityId, ct)
                ?? throw VibeException.NotFound("That idea no longer exists.");

            var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            var plan = new Plan
            {
                Id = Guid.NewGuid(),
                GroupId = request.GroupId,
                ActivityId = request.ActivityId,
                ScheduledAtEpochMillis = request.ScheduledAtEpochMillis,
                CreatedBy = userId,
                UpdatedAtEpochMillis = now,
            };
            db.Plans.Add(plan);
            await db.SaveChangesAsync(ct);

            return Results.Ok(plan.ToDto(activity.Title, activity.Icon));
        })
        .RequireAuthorization()
        .WithTags("Plans and memories")
        .WithSummary("Schedule the winning activity");

        app.MapPost("/api/v1/plans/{planId:guid}/complete", async (
            Guid planId,
            CompletePlanRequest request,
            HttpContext context,
            VibeDbContext db,
            NotificationService notifications,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == planId, ct)
                ?? throw VibeException.NotFound("That plan no longer exists.");

            var group = await GroupEndpoints.LoadAsync(db, plan.GroupId, ct);
            GroupEndpoints.RequireMember(group, userId);

            var problem = Vibe.Api.Security.Validators.Rating(request.Rating)
                ?? Vibe.Api.Security.Validators.Caption(request.Caption);
            if (problem is not null)
            {
                throw VibeException.Validation(problem);
            }

            var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            plan.Completed = true;
            plan.UpdatedAtEpochMillis = now;

            var memory = await db.Memories.FirstOrDefaultAsync(m => m.PlanId == planId, ct);
            if (memory is null)
            {
                memory = new Memory
                {
                    Id = Guid.NewGuid(),
                    GroupId = plan.GroupId,
                    PlanId = plan.Id,
                    ActivityId = plan.ActivityId,
                    CompletedAtEpochMillis = now,
                    Caption = request.Caption,
                    Rating = request.Rating,
                    PhotoUrlsCsv = Mapping.EncodeList(request.PhotoUris ?? []),
                    CreatedBy = userId,
                    UpdatedAtEpochMillis = now,
                };
                db.Memories.Add(memory);
            }
            else
            {
                memory.Caption = request.Caption;
                memory.Rating = request.Rating;
                memory.PhotoUrlsCsv = Mapping.EncodeList(request.PhotoUris ?? []);
                memory.UpdatedAtEpochMillis = now;
            }

            await db.Activities
                .Where(a => a.Id == plan.ActivityId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(a => a.Status, "COMPLETED")
                    .SetProperty(a => a.UpdatedAtEpochMillis, now), ct);

            await db.SaveChangesAsync(ct);

            await notifications.NotifyGroupAsync(
                plan.GroupId,
                "MEMORY",
                "A plan became a memory",
                "Photos, a caption and the group rating are waiting.",
                userId,
                ct);

            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == plan.ActivityId, ct);
            return Results.Ok(memory.ToDto(activity?.Title ?? "Plan", activity?.Icon ?? "🎉"));
        })
        .RequireAuthorization()
        .WithTags("Plans and memories")
        .WithSummary("Mark a plan done and save the memory");

        app.MapGet("/api/v1/groups/{groupId:guid}/memories", async (
            Guid groupId,
            HttpContext context,
            VibeDbContext db,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            var group = await GroupEndpoints.LoadAsync(db, groupId, ct);
            GroupEndpoints.RequireMember(group, userId);

            var memories = await db.Memories
                .Where(m => m.GroupId == groupId)
                .OrderByDescending(m => m.CompletedAtEpochMillis)
                .ToListAsync(ct);

            var activities = await db.Activities
                .Where(a => memories.Select(m => m.ActivityId).Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a, ct);

            return Results.Ok(memories
                .Select(m =>
                {
                    var activity = activities.GetValueOrDefault(m.ActivityId);
                    return m.ToDto(activity?.Title ?? "Memory", activity?.Icon ?? "🎉");
                })
                .ToList());
        })
        .RequireAuthorization()
        .WithTags("Plans and memories")
        .WithSummary("The group's memories");

        app.MapGet("/api/v1/notifications", async (
            HttpContext context,
            NotificationService notifications,
            CancellationToken ct) =>
            Results.Ok(await notifications.InboxAsync(context.User.RequireUserId(), ct)))
        .RequireAuthorization()
        .WithTags("Alerts")
        .WithSummary("The member's inbox (readable offline in the app)");

        app.MapPost("/api/v1/notifications/{notificationId:guid}/read", async (
            Guid notificationId,
            HttpContext context,
            VibeDbContext db,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            await db.Notifications
                .Where(n => n.Id == notificationId && n.UserId == userId)
                .ExecuteUpdateAsync(set => set.SetProperty(n => n.Read, true), ct);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags("Alerts")
        .WithSummary("Mark one alert as read");

        app.MapPost("/api/v1/notifications/read-all", async (
            HttpContext context,
            VibeDbContext db,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            await db.Notifications
                .Where(n => n.UserId == userId && !n.Read)
                .ExecuteUpdateAsync(set => set.SetProperty(n => n.Read, true), ct);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags("Alerts")
        .WithSummary("Mark every alert as read");

        app.MapPost("/api/v1/devices/register", async (
            RegisterDeviceRequest request,
            HttpContext context,
            VibeDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            if (string.IsNullOrWhiteSpace(request.FcmToken))
            {
                throw VibeException.Validation("The push token is missing.", nameof(request.FcmToken));
            }

            var existing = await db.DeviceTokens.FirstOrDefaultAsync(t => t.Token == request.FcmToken, ct);
            if (existing is null)
            {
                db.DeviceTokens.Add(new DeviceToken
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Token = request.FcmToken,
                    Platform = request.Platform,
                    CreatedAtEpochMillis = clock.GetUtcNow().ToUnixTimeMilliseconds(),
                });
            }
            else
            {
                // The same device can be handed to another account: move the token.
                existing.UserId = userId;
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags("Alerts")
        .WithSummary("Register this device for push");

        app.MapPost("/api/v1/sync/batch", async (
            SyncBatchRequest request,
            HttpContext context,
            SyncService sync,
            CancellationToken ct) =>
        {
            var userId = context.User.RequireUserId();
            if (request.Items is null || request.Items.Count == 0)
            {
                return Results.Ok(new SyncBatchResponse([]));
            }

            if (request.Items.Count > 200)
            {
                throw VibeException.Validation("Send at most 200 queued changes per batch.");
            }

            return Results.Ok(await sync.ApplyAsync(userId, request));
        })
        .RequireAuthorization()
        .WithTags("Offline sync")
        .WithSummary("Drain the client's offline queue")
        .Produces<SyncBatchResponse>();
    }

    private static async Task<List<PlanDto>> PlansToDtosAsync(VibeDbContext db, List<Plan> plans, CancellationToken ct)
    {
        var activities = await db.Activities
            .Where(a => plans.Select(p => p.ActivityId).Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a, ct);

        return plans
            .Select(p =>
            {
                var activity = activities.GetValueOrDefault(p.ActivityId);
                return p.ToDto(activity?.Title ?? "Plan", activity?.Icon ?? "🎉");
            })
            .ToList();
    }
}
