using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vibe.Api.Contracts;
using Vibe.Api.Data;
using Vibe.Api.Services;
using Xunit;

namespace Vibe.Tests;

/// <summary>
/// The offline queue, server side: applied changes, conflicts reported instead of
/// overwritten, and a retried batch that is never applied twice.
/// </summary>
public class SyncServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private VibeDbContext _db = null!;
    private SyncService _sync = null!;
    private Guid _userId;
    private Guid _groupId;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VibeDbContext>().UseSqlite(_connection).Options;
        _db = new VibeDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _userId = Guid.NewGuid();
        _groupId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _db.Users.Add(new User
        {
            Id = _userId,
            DisplayName = "Lerato",
            Username = "lerato",
            Email = "lerato@vibe.app",
            PasswordHash = "hash",
            CreatedAtEpochMillis = now,
            UpdatedAtEpochMillis = now,
        });
        _db.Groups.Add(new Group
        {
            Id = _groupId,
            Name = "Friday Night Crew",
            InviteCode = "FRYDAY",
            OwnerId = _userId,
            CreatedAtEpochMillis = now,
            UpdatedAtEpochMillis = now,
        });
        _db.GroupMembers.Add(new GroupMember
        {
            GroupId = _groupId,
            UserId = _userId,
            Role = "OWNER",
            JoinedAtEpochMillis = now,
        });
        await _db.SaveChangesAsync();

        _sync = new SyncService(
            _db,
            TimeProvider.System,
            new NotificationService(_db, TimeProvider.System, new LoggingFcmSender(new NoopLogger<LoggingFcmSender>())));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private SyncItemRequest Queued(string action, string entityId, object payload, long queuedAt) =>
        new($"{action}:{entityId}:{queuedAt}", action, entityId, JsonSerializer.Serialize(payload, Json), queuedAt);

    private long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [Fact]
    public async Task An_offline_idea_is_applied_once()
    {
        var activityId = Guid.NewGuid();
        var item = Queued(
            "activity.add",
            activityId.ToString(),
            new UpsertActivityRequest("Bowling night", Icon: "🎳", ClientId: activityId.ToString(), GroupId: _groupId.ToString()),
            Now());

        var response = await _sync.ApplyAsync(_userId, new SyncBatchRequest([item]));

        Assert.Equal("applied", Assert.Single(response.Results).Status);
        Assert.True(await _db.Activities.AnyAsync(a => a.Id == activityId && a.Title == "Bowling night"));

        // Retrying the same batch must not create a second row.
        var again = await _sync.ApplyAsync(_userId, new SyncBatchRequest([item]));
        Assert.Equal("applied", Assert.Single(again.Results).Status);
        Assert.Equal(1, await _db.Activities.CountAsync());
    }

    [Fact]
    public async Task A_row_that_moved_on_is_reported_as_a_conflict_with_the_server_copy()
    {
        var activityId = Guid.NewGuid();
        var now = Now();
        _db.Activities.Add(new ActivityItem
        {
            Id = activityId,
            GroupId = _groupId,
            Title = "Bowling",
            CreatedBy = _userId,
            CreatedAtEpochMillis = now - 60_000,
            UpdatedAtEpochMillis = now, // changed after the member queued their edit
        });
        await _db.SaveChangesAsync();

        var item = Queued(
            "activity.update",
            activityId.ToString(),
            new UpsertActivityRequest("Bowling night", ClientId: activityId.ToString(), GroupId: _groupId.ToString()),
            now - 5_000);

        var response = await _sync.ApplyAsync(_userId, new SyncBatchRequest([item]));
        var result = Assert.Single(response.Results);

        Assert.Equal("conflict", result.Status);
        Assert.NotNull(result.ServerPayload);
        Assert.Contains("Bowling", result.ServerPayload);
        // Nothing was overwritten: the server row still says "Bowling", not "Bowling night".
        Assert.Equal("Bowling", (await _db.Activities.FindAsync(activityId))!.Title);
    }

    [Fact]
    public async Task An_edit_that_is_newer_than_the_server_row_is_applied()
    {
        var activityId = Guid.NewGuid();
        var now = Now();
        _db.Activities.Add(new ActivityItem
        {
            Id = activityId,
            GroupId = _groupId,
            Title = "Bowling",
            CreatedBy = _userId,
            CreatedAtEpochMillis = now - 120_000,
            UpdatedAtEpochMillis = now - 60_000,
        });
        await _db.SaveChangesAsync();

        var item = Queued(
            "activity.update",
            activityId.ToString(),
            new UpsertActivityRequest("Bowling night", ClientId: activityId.ToString(), GroupId: _groupId.ToString()),
            now - 5_000);

        var response = await _sync.ApplyAsync(_userId, new SyncBatchRequest([item]));

        Assert.Equal("applied", Assert.Single(response.Results).Status);
        Assert.Equal("Bowling night", (await _db.Activities.FindAsync(activityId))!.Title);
    }

    [Fact]
    public async Task A_vote_for_a_round_that_closed_while_the_member_was_offline_is_rejected()
    {
        var decisionId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        var now = Now();
        _db.Activities.Add(new ActivityItem
        {
            Id = activityId,
            GroupId = _groupId,
            Title = "Pizza",
            CreatedBy = _userId,
            CreatedAtEpochMillis = now,
            UpdatedAtEpochMillis = now,
        });
        _db.Decisions.Add(new Decision
        {
            Id = decisionId,
            GroupId = _groupId,
            RoundNumber = 1,
            State = "CLOSED",
            StartedBy = _userId,
            StartedAtEpochMillis = now,
            UpdatedAtEpochMillis = now,
            RoundActivityIdsCsv = activityId.ToString(),
        });
        await _db.SaveChangesAsync();

        var item = Queued(
            "decision.vote",
            $"{decisionId}:{activityId}",
            new CastVoteRequest(activityId, true, now - 10_000, "vote-1"),
            now - 10_000);

        var response = await _sync.ApplyAsync(_userId, new SyncBatchRequest([item]));
        var result = Assert.Single(response.Results);

        Assert.Equal("rejected", result.Status);
        Assert.Contains("closed", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await _db.Votes.CountAsync());
    }

    [Fact]
    public async Task An_edit_from_somebody_outside_the_group_is_rejected()
    {
        var activityId = Guid.NewGuid();
        var now = Now();
        _db.Activities.Add(new ActivityItem
        {
            Id = activityId,
            GroupId = _groupId,
            Title = "Pizza",
            CreatedBy = _userId,
            CreatedAtEpochMillis = now,
            UpdatedAtEpochMillis = now,
        });
        await _db.SaveChangesAsync();

        var stranger = Guid.NewGuid();
        _db.Users.Add(new User
        {
            Id = stranger,
            DisplayName = "Stranger",
            Username = "stranger",
            Email = "stranger@vibe.app",
            PasswordHash = "hash",
            CreatedAtEpochMillis = now,
            UpdatedAtEpochMillis = now,
        });
        await _db.SaveChangesAsync();

        var item = Queued(
            "activity.update",
            activityId.ToString(),
            new UpsertActivityRequest("Hijacked", ClientId: activityId.ToString(), GroupId: _groupId.ToString()),
            now + 1_000);

        var response = await _sync.ApplyAsync(stranger, new SyncBatchRequest([item]));

        Assert.Equal("rejected", Assert.Single(response.Results).Status);
        Assert.Equal("Pizza", (await _db.Activities.FindAsync(activityId))!.Title);
    }
}

/// <summary>Enough of an ILogger for the sender used in tests.</summary>
internal sealed class NoopLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => false;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }
}
