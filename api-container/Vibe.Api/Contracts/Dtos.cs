using Vibe.Api.Data;
using Vibe.Api.Domain;

namespace Vibe.Api.Contracts;

/* ---------------------------------------------------------------- requests */

public record RegisterRequest(string DisplayName, string Email, string Password);
public record LoginRequest(string Email, string Password);
public record GoogleAuthRequest(string IdToken);
public record RefreshRequest(string RefreshToken);
public record PasswordResetRequest(string Email);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record RegisterDeviceRequest(string FcmToken, string Platform = "android");

public record UpdateProfileRequest(
    string? DisplayName = null,
    string? Username = null,
    string? Language = null,
    string? ThemeMode = null,
    bool? NotificationsEnabled = null,
    bool? PrivacyMembersOnly = null,
    string? PhotoUri = null);

public record CreateGroupRequest(string Name, string Icon = "🎉", string? ClientId = null);
public record JoinGroupRequest(string InviteCode);

public record UpsertActivityRequest(
    string Title,
    string Description = "",
    string Icon = "🎉",
    bool? Favourite = null,
    string? Status = null,
    string? ClientId = null);

public record StartDecisionRequest(
    IReadOnlyList<Guid>? ActivityIds = null,
    long? DeadlineEpochMillis = null,
    string? ClientId = null);

public record CastVoteRequest(
    Guid ActivityId,
    bool Choice,
    long ClientTimestampEpochMillis,
    string ClientId);

public record SurpriseRequest(IReadOnlyList<Guid>? RejectedIds = null);
public record SavePlanRequest(Guid GroupId, Guid ActivityId, long? ScheduledAtEpochMillis);
public record CompletePlanRequest(int Rating, string Caption, IReadOnlyList<string>? PhotoUris = null);

public record SyncItemRequest(
    string ClientId,
    string Action,
    string EntityId,
    string PayloadJson,
    long QueuedAtEpochMillis);

public record SyncBatchRequest(IReadOnlyList<SyncItemRequest> Items);

/* --------------------------------------------------------------- responses */

public record AuthResponse(string AccessToken, string RefreshToken, long ExpiresAtEpochMillis, UserDto User);

public record UserDto(
    Guid Id,
    string DisplayName,
    string Username,
    string Email,
    string Language,
    string ThemeMode,
    bool NotificationsEnabled,
    bool PrivacyMembersOnly,
    string? PhotoUri,
    long CreatedAtEpochMillis);

public record MemberDto(Guid UserId, string DisplayName, string Role, string? PhotoUri, long JoinedAtEpochMillis);

public record GroupDto(
    Guid Id,
    string Name,
    string Icon,
    string InviteCode,
    Guid OwnerId,
    long CreatedAtEpochMillis,
    string Role,
    int MemberCount,
    int ActivityCount,
    IReadOnlyList<MemberDto> Members);

public record ActivityDto(
    Guid Id,
    Guid GroupId,
    string Title,
    string Description,
    string Icon,
    string Status,
    Guid CreatedBy,
    string CreatedByName,
    long CreatedAtEpochMillis,
    bool Favourite,
    int YesVotes,
    int ParticipantCount);

public record TallyDto(Guid ActivityId, int Yes, int No, int Pending);

public record ParticipationDto(int MemberCount, int VotedCount, IReadOnlyList<Guid> PendingMemberIds);

public record DecisionDto(
    Guid Id,
    Guid GroupId,
    int RoundNumber,
    string State,
    long? DeadlineEpochMillis,
    Guid? WinnerActivityId,
    Guid StartedBy,
    long StartedAtEpochMillis,
    ParticipationDto Participation,
    IReadOnlyList<TallyDto> Tallies,
    IReadOnlyList<Guid> SurvivorIds,
    IReadOnlyList<Guid> EliminatedIds,
    bool VotesRevealed,
    bool CanCloseEarly,
    string? Outcome);

public record VoteAckDto(Guid VoteId, DecisionDto Decision);

public record PlanDto(
    Guid Id,
    Guid GroupId,
    Guid ActivityId,
    string ActivityTitle,
    string ActivityIcon,
    long? ScheduledAtEpochMillis,
    bool Completed,
    Guid CreatedBy);

public record MemoryDto(
    Guid Id,
    Guid GroupId,
    Guid PlanId,
    Guid ActivityId,
    string ActivityTitle,
    string ActivityIcon,
    long CompletedAtEpochMillis,
    string Caption,
    int Rating,
    IReadOnlyList<string> PhotoUris,
    Guid CreatedBy);

public record NotificationDto(
    Guid Id,
    Guid UserId,
    string Title,
    string Body,
    string Type,
    bool Read,
    long CreatedAtEpochMillis,
    Guid? GroupId);

public record SyncResultDto(string ClientId, string Status, string? ServerPayload, string? Message);

public record SyncBatchResponse(IReadOnlyList<SyncResultDto> Results);

/// <summary>Every failure answers with a message and, when relevant, the field it belongs to.</summary>
public record ErrorBody(string Error, string? Field = null);

/* ----------------------------------------------------------------- mapping */

public static class Mapping
{
    public static UserDto ToDto(this User user) => new(
        user.Id,
        user.DisplayName,
        user.Username,
        user.Email,
        user.Language,
        user.ThemeMode,
        user.NotificationsEnabled,
        user.PrivacyMembersOnly,
        user.PhotoUri,
        user.CreatedAtEpochMillis);

    public static MemberDto ToDto(this GroupMember member) => new(
        member.UserId,
        member.User?.DisplayName ?? "Member",
        member.Role,
        member.User?.PhotoUri,
        member.JoinedAtEpochMillis);

    public static GroupDto ToDto(this Group group, Guid currentUserId, int activityCount) => new(
        group.Id,
        group.Name,
        group.Icon,
        group.InviteCode,
        group.OwnerId,
        group.CreatedAtEpochMillis,
        group.Members.FirstOrDefault(m => m.UserId == currentUserId)?.Role ?? "MEMBER",
        group.Members.Count,
        activityCount,
        group.Members.Select(m => m.ToDto()).ToList());

    public static ActivityDto ToDto(this ActivityItem activity, string createdByName = "") => new(
        activity.Id,
        activity.GroupId,
        activity.Title,
        activity.Description,
        activity.Icon,
        activity.Status,
        activity.CreatedBy,
        createdByName,
        activity.CreatedAtEpochMillis,
        activity.Favourite,
        activity.YesVotes,
        activity.ParticipantCount);

    public static TallyDto ToDto(this Tally tally) => new(tally.ActivityId, tally.Yes, tally.No, tally.Pending);

    public static ParticipationDto ToDto(this Participation participation) => new(
        participation.MemberCount,
        participation.VotedCount,
        participation.PendingMemberIds);

    /// <summary>Builds the wire decision the client renders, honouring the "no results before close" rule.</summary>
    public static DecisionDto ToDto(this Decision decision, RoundEvaluation? evaluation, IReadOnlyList<ActivityIdea>? ideas = null)
    {
        var revealed = evaluation?.VotesRevealed ?? decision.State is "CLOSED" or "SETTLED";
        var tallies = revealed
            ? evaluation?.Tallies.Select(t => t.ToDto()).ToList() ?? []
            : [];

        var survivors = evaluation is { VotesRevealed: true }
            ? evaluation.SurvivorIds
            : (ideas ?? []).Select(i => i.Id).ToList();

        return new DecisionDto(
            decision.Id,
            decision.GroupId,
            decision.RoundNumber,
            decision.State,
            decision.DeadlineEpochMillis,
            decision.WinnerActivityId,
            decision.StartedBy,
            decision.StartedAtEpochMillis,
            evaluation?.Participation.ToDto() ?? new ParticipationDto(),
            tallies,
            survivors,
            evaluation is { VotesRevealed: true } ? evaluation.EliminatedIds : [],
            revealed,
            evaluation?.CanCloseEarly ?? false,
            evaluation?.Outcome.Wire);
    }

    public static PlanDto ToDto(this Plan plan, string activityTitle, string activityIcon) => new(
        plan.Id,
        plan.GroupId,
        plan.ActivityId,
        activityTitle,
        activityIcon,
        plan.ScheduledAtEpochMillis,
        plan.Completed,
        plan.CreatedBy);

    public static MemoryDto ToDto(this Memory memory, string activityTitle, string activityIcon) => new(
        memory.Id,
        memory.GroupId,
        memory.PlanId,
        memory.ActivityId,
        activityTitle,
        activityIcon,
        memory.CompletedAtEpochMillis,
        memory.Caption,
        memory.Rating,
        DecodeList(memory.PhotoUrlsCsv),
        memory.CreatedBy);

    public static NotificationDto ToDto(this Notification notification) => new(
        notification.Id,
        notification.UserId,
        notification.Title,
        notification.Body,
        notification.Type,
        notification.Read,
        notification.CreatedAtEpochMillis,
        notification.GroupId);

    public static string EncodeList(IEnumerable<string?> values) => string.Join(',', values.Where(v => !string.IsNullOrWhiteSpace(v)));

    public static IReadOnlyList<string> DecodeList(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
