using Microsoft.EntityFrameworkCore;
using Vibe.Api.Contracts;
using Vibe.Api.Data;
using Vibe.Api.Domain;
using Vibe.Api.Infrastructure;
using Vibe.Api.Security;

namespace Vibe.Api.Services;

/// <summary>
/// Runs the decision game on the server.
/// </summary>
/// <remarks>
/// All rules live in <see cref="DecisionEngine"/>; this class only loads the
/// members, the options of the round and the votes, and writes the result back:
/// eliminations, the survivor list, the winner and the next round. Because the
/// client evaluates the same inputs with the same algorithm, a round decided in
/// airplane mode is identical to the one the server computes later.
/// </remarks>
public class DecisionService(
    VibeDbContext db,
    TimeProvider clock,
    NotificationService notifications)
{
    private static readonly TimeSpan DefaultDeadline = TimeSpan.FromHours(48);

    public async Task<DecisionDto> StartRoundAsync(Guid groupId, Guid userId, StartDecisionRequest request)
    {
        var group = await LoadGroupAsync(groupId);
        RequireOwner(group, userId);

        var ideas = await EligibleIdeasAsync(groupId, request.ActivityIds);
        var problem = Validators.RoundActivities(ideas.Count);
        if (problem is not null)
        {
            throw VibeException.Validation(problem);
        }

        var now = Now();
        var decision = new Decision
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            RoundNumber = 1,
            State = "OPEN",
            DeadlineEpochMillis = request.DeadlineEpochMillis ?? now + (long)DefaultDeadline.TotalMilliseconds,
            StartedBy = userId,
            StartedAtEpochMillis = now,
            UpdatedAtEpochMillis = now,
            ClientId = request.ClientId,
            RoundActivityIdsCsv = string.Join(',', ideas.Select(i => i.Id)),
        };

        foreach (var idea in ideas)
        {
            var activity = await db.Activities.FirstAsync(a => a.Id == idea.Id);
            activity.Status = "ACTIVE";
            activity.UpdatedAtEpochMillis = now;
        }

        db.Decisions.Add(decision);
        await db.SaveChangesAsync();

        await notifications.NotifyGroupAsync(
            groupId,
            "DEADLINE",
            $"Round 1 is open in {group.Name}",
            $"Cast your YES or NO vote on {ideas.Count} ideas.",
            userId);

        return decision.ToDto(await EvaluateAsync(decision), ideas);
    }

    /// <summary>One YES/NO vote per member per activity; a later vote replaces the earlier one.</summary>
    public async Task<VoteAckDto> CastVoteAsync(Guid decisionId, Guid userId, CastVoteRequest request)
    {
        var decision = await db.Decisions.FirstOrDefaultAsync(d => d.Id == decisionId)
            ?? throw VibeException.NotFound("That round no longer exists.");

        await RequireMemberAsync(decision.GroupId, userId);

        if (decision.State != "OPEN")
        {
            throw VibeException.Conflict("This round is already closed.");
        }

        var roundIds = DecodeIds(decision.RoundActivityIdsCsv);
        if (!roundIds.Contains(request.ActivityId))
        {
            throw VibeException.Validation("That idea is not part of this round.", "activityId");
        }

        var existing = await db.Votes.FirstOrDefaultAsync(v =>
            v.DecisionId == decisionId && v.ActivityId == request.ActivityId && v.UserId == userId);

        var castAt = request.ClientTimestampEpochMillis > 0 ? request.ClientTimestampEpochMillis : Now();
        if (existing is null)
        {
            existing = new Vote
            {
                DecisionId = decisionId,
                ActivityId = request.ActivityId,
                UserId = userId,
                Choice = request.Choice,
                CastAtEpochMillis = castAt,
            };
            db.Votes.Add(existing);
        }
        else
        {
            existing.Choice = request.Choice;
            existing.CastAtEpochMillis = castAt;
        }

        decision.UpdatedAtEpochMillis = Now();
        await db.SaveChangesAsync();

        var settled = await SettleIfFinishedAsync(decision);
        return new VoteAckDto(Guid.NewGuid(), settled);
    }

    /// <summary>Closes the round, eliminates what cannot progress and opens the next one.</summary>
    public async Task<DecisionDto> CloseAsync(Guid decisionId, Guid userId)
    {
        var decision = await db.Decisions.FirstOrDefaultAsync(d => d.Id == decisionId)
            ?? throw VibeException.NotFound("That round no longer exists.");

        var group = await LoadGroupAsync(decision.GroupId);
        if (group.OwnerId != userId)
        {
            // A member may still close a round that the engine already decided.
            var evaluation = await EvaluateAsync(decision);
            if (!evaluation.CanCloseEarly)
            {
                throw VibeException.Forbidden("Only the group owner can close this round.");
            }
        }

        if (decision.State == "OPEN")
        {
            decision.State = "CLOSED";
            decision.UpdatedAtEpochMillis = Now();
            await db.SaveChangesAsync();
        }

        return await SettleIfFinishedAsync(decision);
    }

    /// <summary>"Surprise Me": a random option that can still win.</summary>
    public async Task<ActivityDto> SurpriseAsync(Guid decisionId, Guid userId, SurpriseRequest request)
    {
        var decision = await db.Decisions.FirstOrDefaultAsync(d => d.Id == decisionId)
            ?? throw VibeException.NotFound("That round no longer exists.");

        await RequireMemberAsync(decision.GroupId, userId);

        var ideas = await RoundIdeasAsync(decision);
        var pick = DecisionEngine.SurpriseMe(ideas, request.RejectedIds)
            ?? throw VibeException.Validation("No ideas left to surprise you with - add one to the Vibe List.");

        var activity = await db.Activities.FirstAsync(a => a.Id == pick.Id);
        return activity.ToDto(await DisplayNameAsync(activity.CreatedBy));
    }

    public async Task<DecisionDto> GetAsync(Guid decisionId, Guid userId)
    {
        var decision = await db.Decisions.FirstOrDefaultAsync(d => d.Id == decisionId)
            ?? throw VibeException.NotFound("That round no longer exists.");

        await RequireMemberAsync(decision.GroupId, userId);
        return decision.ToDto(await EvaluateAsync(decision), await RoundIdeasAsync(decision));
    }

    /* ------------------------------------------------------------- internals */

    /// <summary>Settles a closed round: eliminate, advance, or hand out the win.</summary>
    private async Task<DecisionDto> SettleIfFinishedAsync(Decision decision)
    {
        var ideas = await RoundIdeasAsync(decision);
        var evaluation = await EvaluateAsync(decision, ideas);

        if (evaluation.Outcome is RoundOutcome.InProgress)
        {
            return decision.ToDto(evaluation, ideas);
        }

        var now = Now();
        switch (evaluation.Outcome)
        {
            case RoundOutcome.ContinueRounds next:
                await ApplyEliminationsAsync(evaluation, now);
                var carry = next.SurvivorIds.ToList();
                decision.State = "CLOSED";
                decision.UpdatedAtEpochMillis = now;
                await db.SaveChangesAsync();

                var round = new Decision
                {
                    Id = Guid.NewGuid(),
                    GroupId = decision.GroupId,
                    RoundNumber = next.NextRoundNumber,
                    State = "OPEN",
                    DeadlineEpochMillis = now + (long)DefaultDeadline.TotalMilliseconds,
                    StartedBy = decision.StartedBy,
                    StartedAtEpochMillis = now,
                    UpdatedAtEpochMillis = now,
                    RoundActivityIdsCsv = string.Join(',', carry),
                };
                db.Decisions.Add(round);
                await db.SaveChangesAsync();
                return round.ToDto(await EvaluateAsync(round), carry.Select(id => ideas.First(i => i.Id == id)).ToList());

            case RoundOutcome.Winner winner:
                await ApplyEliminationsAsync(evaluation, now);
                decision.State = "SETTLED";
                decision.WinnerActivityId = winner.ActivityId;
                decision.UpdatedAtEpochMillis = now;
                await db.Activities
                    .Where(a => a.Id == winner.ActivityId)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(a => a.Status, "WINNER")
                        .SetProperty(a => a.UpdatedAtEpochMillis, now));
                await db.SaveChangesAsync();

                await notifications.NotifyGroupAsync(
                    decision.GroupId,
                    "WINNER",
                    "The group has a winner",
                    $"{ideas.First(i => i.Id == winner.ActivityId).Title} won after {decision.RoundNumber} round(s).",
                    Guid.Empty);
                return decision.ToDto(await EvaluateAsync(decision), ideas);

            case RoundOutcome.Tie tie:
                await ApplyEliminationsAsync(evaluation, now);
                decision.State = "CLOSED";
                decision.UpdatedAtEpochMillis = now;
                await db.SaveChangesAsync();
                return decision.ToDto(evaluation, tie.SurvivorIds.Select(id => ideas.First(i => i.Id == id)).ToList());

            default: // NoWinner - nothing could progress, so the group needs fresh ideas.
                await ApplyEliminationsAsync(evaluation, now);
                decision.State = "CLOSED";
                decision.UpdatedAtEpochMillis = now;
                await db.SaveChangesAsync();
                return decision.ToDto(evaluation, ideas);
        }
    }

    private async Task ApplyEliminationsAsync(RoundEvaluation evaluation, long now)
    {
        var eliminated = evaluation.EliminatedIds.ToList();
        if (eliminated.Count == 0)
        {
            return;
        }

        await db.Activities
            .Where(a => eliminated.Contains(a.Id))
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.Status, "ELIMINATED")
                .SetProperty(a => a.UpdatedAtEpochMillis, now));
    }

    private async Task<RoundEvaluation> EvaluateAsync(Decision decision, IReadOnlyList<ActivityIdea>? ideas = null)
    {
        ideas ??= await RoundIdeasAsync(decision);
        var memberIds = await db.GroupMembers
            .Where(m => m.GroupId == decision.GroupId)
            .Select(m => m.UserId)
            .ToListAsync();

        var votes = await db.Votes
            .Where(v => v.DecisionId == decision.Id)
            .Select(v => new VoteChoice(v.ActivityId, v.UserId, v.Choice, v.CastAtEpochMillis))
            .ToListAsync();

        var evaluation = DecisionEngine.Evaluate(new RoundInput(
            decision.RoundNumber,
            decision.State switch
            {
                "OPEN" => RoundState.Open,
                "CLOSED" => RoundState.Closed,
                _ => RoundState.Settled,
            },
            Now(),
            memberIds,
            ideas,
            votes,
            decision.DeadlineEpochMillis));

        await UpdateCountsAsync(evaluation);
        return evaluation;
    }

    /// <summary>Keeps the share of YES votes and the participant count on the Vibe List row.</summary>
    private async Task UpdateCountsAsync(RoundEvaluation evaluation)
    {
        if (evaluation.Tallies.Count == 0)
        {
            return;
        }

        foreach (var tally in evaluation.Tallies)
        {
            var yes = tally.Yes;
            var participants = tally.Cast;
            await db.Activities
                .Where(a => a.Id == tally.ActivityId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(a => a.YesVotes, yes)
                    .SetProperty(a => a.ParticipantCount, participants));
        }

        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<ActivityIdea>> RoundIdeasAsync(Decision decision)
    {
        var ids = DecodeIds(decision.RoundActivityIdsCsv);
        if (ids.Count == 0)
        {
            return await EligibleIdeasAsync(decision.GroupId, null);
        }

        var rows = await db.Activities.Where(a => ids.Contains(a.Id)).ToListAsync();
        return rows
            .OrderBy(a => ids.IndexOf(a.Id))
            .Select(a => new ActivityIdea(a.Id, a.Title, a.Icon, a.Status, a.CreatedAtEpochMillis))
            .ToList();
    }

    private async Task<IReadOnlyList<ActivityIdea>> EligibleIdeasAsync(Guid groupId, IReadOnlyList<Guid>? only)
    {
        var query = db.Activities.Where(a => a.GroupId == groupId && (a.Status == "SUGGESTED" || a.Status == "ACTIVE"));
        if (only is { Count: > 0 })
        {
            query = query.Where(a => only.Contains(a.Id));
        }

        var rows = await query.OrderBy(a => a.CreatedAtEpochMillis).ToListAsync();
        return rows.Select(a => new ActivityIdea(a.Id, a.Title, a.Icon, a.Status, a.CreatedAtEpochMillis)).ToList();
    }

    private async Task<Group> LoadGroupAsync(Guid groupId) =>
        await db.Groups
            .Include(g => g.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(g => g.Id == groupId)
        ?? throw VibeException.NotFound("That group no longer exists.");

    private static void RequireOwner(Group group, Guid userId)
    {
        if (group.OwnerId != userId)
        {
            throw VibeException.Forbidden("Only the group owner can start a round.");
        }
    }

    private async Task RequireMemberAsync(Guid groupId, Guid userId)
    {
        var isMember = await db.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == userId);
        if (!isMember)
        {
            throw VibeException.Forbidden("You are not a member of this group.");
        }
    }

    private async Task<string> DisplayNameAsync(Guid userId) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync() ?? "Member";

    private long Now() => clock.GetUtcNow().ToUnixTimeMilliseconds();

    private static List<Guid> DecodeIds(string csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Guid.Parse)
                .ToList();
}
