using Microsoft.EntityFrameworkCore;
using Vibe.Api.Contracts;
using Vibe.Api.Data;

namespace Vibe.Api.Services;

/// <summary>Sends the push message. A deployment plugs Firebase Cloud Messaging in here.</summary>
public interface IFcmSender
{
    Task SendAsync(IReadOnlyList<string> deviceTokens, string title, string body, string type, CancellationToken cancellationToken = default);
}

/// <summary>
/// Logs the push instead of sending it, so the API runs and the flows are
/// demonstrable before Firebase credentials exist. Replace the registration in
/// <c>Program.cs</c> with an <c>FcmSender</c> that posts to
/// <c>https://fcm.googleapis.com/v1/projects/{project}/messages:send</c>.
/// </summary>
public class LoggingFcmSender(ILogger<LoggingFcmSender> logger) : IFcmSender
{
    public Task SendAsync(
        IReadOnlyList<string> deviceTokens,
        string title,
        string body,
        string type,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "FCM ({Type}) to {Count} device(s): {Title} - {Body}",
            type,
            deviceTokens.Count,
            title,
            body);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Alerts ("invitations, new options, vote deadlines, winners").
/// </summary>
/// <remarks>
/// Every alert is written to the member's inbox first and then pushed, so the
/// message is still readable in the app when the push never arrives - which is the
/// normal case on a campus network at load-shedding o'clock.
/// </remarks>
public class NotificationService(
    VibeDbContext db,
    TimeProvider clock,
    IFcmSender sender)
{
    public async Task NotifyGroupAsync(
        Guid groupId,
        string type,
        string title,
        string body,
        Guid excludeUserId,
        CancellationToken cancellationToken = default)
    {
        var recipients = await db.GroupMembers
            .Where(m => m.GroupId == groupId && m.UserId != excludeUserId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => u)
            .Where(u => u.NotificationsEnabled)
            .ToListAsync(cancellationToken);

        if (recipients.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var alerts = recipients.Select(user => new Vibe.Api.Data.Notification
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Title = title,
            Body = body,
            Type = type,
            Read = false,
            CreatedAtEpochMillis = now,
            GroupId = groupId,
        }).ToList();

        db.Notifications.AddRange(alerts);
        await db.SaveChangesAsync(cancellationToken);

        var tokens = await db.DeviceTokens
            .Where(t => recipients.Select(r => r.Id).Contains(t.UserId))
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);

        if (tokens.Count > 0)
        {
            await sender.SendAsync(tokens, title, body, type, cancellationToken);
        }
    }

    public async Task NotifyUserAsync(
        Guid userId,
        string type,
        string title,
        string body,
        Guid? groupId = null,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        db.Notifications.Add(new Vibe.Api.Data.Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Body = body,
            Type = type,
            Read = false,
            CreatedAtEpochMillis = now,
            GroupId = groupId,
        });
        await db.SaveChangesAsync(cancellationToken);

        var tokens = await db.DeviceTokens
            .Where(t => t.UserId == userId)
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);

        if (tokens.Count > 0)
        {
            await sender.SendAsync(tokens, title, body, type, cancellationToken);
        }
    }

    public async Task<List<NotificationDto>> InboxAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAtEpochMillis)
            .Take(100)
            .Select(n => new NotificationDto(
                n.Id,
                n.UserId,
                n.Title,
                n.Body,
                n.Type,
                n.Read,
                n.CreatedAtEpochMillis,
                n.GroupId))
            .ToListAsync(cancellationToken);
}
