using System.Security.Cryptography;

namespace Vibe.Api.Domain;

/// <summary>
/// "Decide For Us" - the same algorithm as the Android client
/// (<c>app/src/main/java/com/vibe/app/domain/DecisionEngine.kt</c>).
/// </summary>
/// <remarks>
/// Keeping two implementations is deliberate: the client must be able to decide a
/// round while it is offline, and the server must be able to settle the same round
/// when the group reconnects. They agree because both are written from this rule
/// list and both are covered by tests:
///
/// <list type="number">
/// <item>only SUGGESTED and ACTIVE options enter a round;</item>
/// <item>every member casts exactly one YES/NO vote per activity - a later vote
/// replaces the earlier one and votes from outside the group are ignored;</item>
/// <item>an option progresses when YES &gt; NO;</item>
/// <item>an option that cannot progress is eliminated from the next round;</item>
/// <item>nothing is revealed and no winner is claimed before the round closes, but
/// the round may close early once at most one option could still win;</item>
/// <item>survivors go into the next round (at most <see cref="MaxRounds"/>);
/// otherwise the result is a winner, a tie or no winner at all.</item>
/// </list>
/// </remarks>
public static class DecisionEngine
{
    public const int MaxRounds = 5;

    public static RoundEvaluation Evaluate(RoundInput input)
    {
        var memberIds = input.MemberIds.Distinct().ToList();
        var memberSet = memberIds.ToHashSet();

        // Ignore options that have already been decided or finished.
        var eligible = input.Activities
            .Where(a => a.Status is "SUGGESTED" or "ACTIVE")
            .ToList();

        var tallies = eligible.Select(idea =>
        {
            var perMember = input.Votes
                .Where(v => v.ActivityId == idea.Id && memberSet.Contains(v.UserId))
                .GroupBy(v => v.UserId)
                .Select(group => group.MaxBy(v => v.CastAtEpochMillis)!); // one vote per member

            var votes = perMember.ToList();
            return new Tally(
                idea.Id,
                votes.Count(v => v.Choice),
                votes.Count(v => !v.Choice),
                memberIds.Count(id => votes.All(v => v.UserId != id)));
        }).ToList();

        var votedMemberIds = input.Votes
            .Where(v => memberSet.Contains(v.UserId))
            .Select(v => v.UserId)
            .Distinct()
            .ToHashSet();

        var participation = new Participation(
            memberIds.Count,
            votedMemberIds.Count,
            memberIds.Where(id => !votedMemberIds.Contains(id)).ToList());

        var deadlinePassed = input.DeadlineEpochMillis is { } deadline && input.NowEpochMillis >= deadline;
        var roundOver = input.State != RoundState.Open || participation.EveryoneVoted || deadlinePassed;

        var stillPossible = tallies.Count(t => t.CouldStillWin);
        var canCloseEarly = !roundOver && stillPossible <= 1;

        if (!roundOver)
        {
            // Results stay hidden: the group only learns whether more votes are useful.
            return new RoundEvaluation(
                input.RoundNumber,
                new RoundOutcome.InProgress(participation.PendingMemberIds),
                tallies,
                eligible,
                Array.Empty<ActivityIdea>(),
                participation,
                VotesRevealed: false,
                CanCloseEarly: canCloseEarly);
        }

        var tallyById = tallies.ToDictionary(t => t.ActivityId);
        var survivors = eligible.Where(a => tallyById[a.Id].CanProgress).ToList();
        var eliminated = eligible.Where(a => !survivors.Contains(a)).ToList();

        RoundOutcome outcome = survivors.Count switch
        {
            1 => new RoundOutcome.Winner(survivors[0].Id),
            0 => new RoundOutcome.NoWinner(),
            _ when input.RoundNumber >= input.MaxRounds => new RoundOutcome.Tie(survivors.Select(a => a.Id).ToList()),
            _ => new RoundOutcome.ContinueRounds(survivors.Select(a => a.Id).ToList(), input.RoundNumber + 1),
        };

        return new RoundEvaluation(
            input.RoundNumber,
            outcome,
            tallies,
            survivors,
            eliminated,
            participation,
            VotesRevealed: true,
            CanCloseEarly: canCloseEarly);
    }

    /// <summary>The vote summary the winner screen shows.</summary>
    public static Tally WinnerTally(RoundEvaluation evaluation, Guid winnerActivityId) =>
        evaluation.Tallies.FirstOrDefault(t => t.ActivityId == winnerActivityId)
        ?? new Tally(winnerActivityId, 0, 0, evaluation.Participation.MemberCount);

    /// <summary>"Surprise Me": one eligible activity at random.</summary>
    public static ActivityIdea? SurpriseMe(
        IEnumerable<ActivityIdea> candidates,
        IEnumerable<Guid>? rejectedIds = null,
        Random? random = null)
    {
        var rejected = rejectedIds?.ToHashSet() ?? new HashSet<Guid>();
        var pool = candidates
            .Where(a => a.Status is "SUGGESTED" or "ACTIVE" && !rejected.Contains(a.Id))
            .ToList();

        if (pool.Count == 0)
        {
            return null;
        }

        return pool[(random ?? Random.Shared).Next(pool.Count)];
    }

    /// <summary>Six characters without I, O, 0 or 1, so a code can be read out loud.</summary>
    public static string GenerateInviteCode(Random? random = null)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rng = random ?? Random.Shared;
        return string.Create(6, rng, (span, r) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = alphabet[r.Next(alphabet.Length)];
            }
        });
    }

    /// <summary>A stable, unguessable id for a group the client created offline.</summary>
    public static string NewToken(int bytes = 32) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));
}
