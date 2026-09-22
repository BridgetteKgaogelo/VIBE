using Vibe.Api.Domain;
using Xunit;

namespace Vibe.Tests;

/// <summary>
/// Parity tests for the decision algorithm. The Android client runs the same rules
/// (<c>app/src/test/java/com/vibe/app/domain/DecisionEngineTest.kt</c>), so these
/// cases exist twice on purpose: if the two implementations drift, one of the two
/// suites fails.
/// </summary>
public class DecisionEngineTests
{
    private static readonly Guid Lerato = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Thandi = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Sipho = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Naledi = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly Guid Pizza = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid Bowling = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111");
    private static readonly Guid Movie = Guid.Parse("cccccccc-1111-1111-1111-111111111111");

    private static ActivityIdea Idea(Guid id, string status = "ACTIVE") => new(id, id.ToString(), "🎉", status);

    private static VoteChoice Yes(Guid activity, Guid user, long at = 0) => new(activity, user, true, at);

    private static VoteChoice No(Guid activity, Guid user, long at = 0) => new(activity, user, false, at);

    private static RoundInput Round(
        RoundState state = RoundState.Open,
        int roundNumber = 1,
        long? deadline = null,
        long now = 1_000,
        IReadOnlyList<Guid>? members = null,
        IReadOnlyList<ActivityIdea>? activities = null,
        IReadOnlyList<VoteChoice>? votes = null) =>
        new(
            roundNumber,
            state,
            now,
            members ?? [Lerato, Thandi, Sipho],
            activities ?? [Idea(Pizza), Idea(Bowling)],
            votes ?? [],
            deadline);

    [Fact]
    public void Results_stay_hidden_while_the_round_is_open()
    {
        var evaluation = DecisionEngine.Evaluate(Round(votes: [Yes(Pizza, Lerato)]));

        Assert.IsType<RoundOutcome.InProgress>(evaluation.Outcome);
        Assert.False(evaluation.VotesRevealed);
        Assert.Equal(1, evaluation.Participation.VotedCount);
        Assert.Equal(3, evaluation.Participation.MemberCount);
        Assert.Equal(2, Assert.IsType<RoundOutcome.InProgress>(evaluation.Outcome).WaitingForMemberIds.Count);
    }

    [Fact]
    public void The_option_with_more_yes_than_no_wins_once_everyone_voted()
    {
        var evaluation = DecisionEngine.Evaluate(Round(votes:
        [
            Yes(Pizza, Lerato), Yes(Pizza, Thandi), No(Pizza, Sipho),
            No(Bowling, Lerato), No(Bowling, Thandi), Yes(Bowling, Sipho),
        ]));

        Assert.Equal(new RoundOutcome.Winner(Pizza), evaluation.Outcome);
        Assert.True(evaluation.VotesRevealed);
        Assert.Equal([Bowling], evaluation.EliminatedIds);
        Assert.True(evaluation.Participation.EveryoneVoted);
    }

    [Fact]
    public void Options_that_cannot_progress_are_eliminated_and_survivors_continue()
    {
        var evaluation = DecisionEngine.Evaluate(Round(
            activities: [Idea(Pizza), Idea(Bowling), Idea(Movie)],
            votes:
            [
                Yes(Pizza, Lerato), Yes(Pizza, Thandi), Yes(Pizza, Sipho),
                Yes(Bowling, Lerato), Yes(Bowling, Thandi), No(Bowling, Sipho),
                No(Movie, Lerato), No(Movie, Thandi), No(Movie, Sipho),
            ]));

        var next = Assert.IsType<RoundOutcome.ContinueRounds>(evaluation.Outcome);
        Assert.Equal([Pizza, Bowling], next.SurvivorIds);
        Assert.Equal(2, next.NextRoundNumber);
        Assert.Equal([Movie], evaluation.EliminatedIds);
    }

    [Fact]
    public void A_level_round_eliminates_every_option()
    {
        var evaluation = DecisionEngine.Evaluate(Round(votes:
        [
            Yes(Pizza, Lerato), No(Pizza, Thandi), No(Pizza, Sipho),
            Yes(Bowling, Lerato), No(Bowling, Thandi), No(Bowling, Sipho),
        ]));

        Assert.IsType<RoundOutcome.NoWinner>(evaluation.Outcome);
        Assert.Equal(2, evaluation.EliminatedIds.Count);
    }

    [Fact]
    public void The_final_comparison_reports_a_tie_instead_of_inventing_a_winner()
    {
        var evaluation = DecisionEngine.Evaluate(Round(
            roundNumber: DecisionEngine.MaxRounds,
            votes:
            [
                Yes(Pizza, Lerato), Yes(Pizza, Thandi), No(Pizza, Sipho),
                Yes(Bowling, Lerato), Yes(Bowling, Thandi), No(Bowling, Sipho),
            ]));

        var tie = Assert.IsType<RoundOutcome.Tie>(evaluation.Outcome);
        Assert.Equal([Pizza, Bowling], tie.SurvivorIds);
    }

    [Fact]
    public void The_round_can_close_early_when_only_one_option_can_still_win()
    {
        var evaluation = DecisionEngine.Evaluate(Round(
            members: [Lerato, Thandi, Sipho, Naledi],
            activities: [Idea(Pizza), Idea(Bowling), Idea(Movie)],
            votes:
            [
                Yes(Pizza, Lerato), Yes(Pizza, Thandi), Yes(Pizza, Sipho),
                No(Bowling, Lerato), No(Bowling, Thandi), No(Bowling, Sipho),
                Yes(Movie, Lerato), No(Movie, Thandi), No(Movie, Sipho),
            ]));

        Assert.True(evaluation.CanCloseEarly);
        Assert.IsType<RoundOutcome.InProgress>(evaluation.Outcome);
        Assert.False(evaluation.VotesRevealed);

        var closed = DecisionEngine.Evaluate(Round(
            state: RoundState.Closed,
            members: [Lerato, Thandi, Sipho, Naledi],
            activities: [Idea(Pizza), Idea(Bowling), Idea(Movie)],
            votes:
            [
                Yes(Pizza, Lerato), Yes(Pizza, Thandi), Yes(Pizza, Sipho),
                No(Bowling, Lerato), No(Bowling, Thandi), No(Bowling, Sipho),
                Yes(Movie, Lerato), No(Movie, Thandi), No(Movie, Sipho),
            ]));

        Assert.Equal(new RoundOutcome.Winner(Pizza), closed.Outcome);
        Assert.True(closed.VotesRevealed);
    }

    [Fact]
    public void A_passed_deadline_closes_the_round_without_every_vote()
    {
        var evaluation = DecisionEngine.Evaluate(Round(
            deadline: 500,
            now: 900,
            votes: [Yes(Pizza, Lerato), No(Pizza, Thandi), Yes(Bowling, Lerato), No(Bowling, Thandi)]));

        Assert.True(evaluation.VotesRevealed);
        Assert.IsType<RoundOutcome.NoWinner>(evaluation.Outcome);
        Assert.False(evaluation.CanCloseEarly);
    }

    [Fact]
    public void A_later_vote_replaces_the_earlier_one_and_outsiders_never_count()
    {
        var evaluation = DecisionEngine.Evaluate(Round(
            members: [Lerato],
            activities: [Idea(Pizza)],
            votes: [No(Pizza, Lerato, at: 10), Yes(Pizza, Lerato, at: 20), Yes(Pizza, Guid.NewGuid())]));

        var tally = Assert.Single(evaluation.Tallies);
        Assert.Equal(1, tally.Yes);
        Assert.Equal(0, tally.No);
        Assert.Equal(new RoundOutcome.Winner(Pizza), evaluation.Outcome);
    }

    [Fact]
    public void Finished_options_never_enter_a_round()
    {
        var evaluation = DecisionEngine.Evaluate(Round(
            members: [Lerato],
            activities: [Idea(Pizza), Idea(Bowling, "COMPLETED"), Idea(Movie, "ELIMINATED")],
            votes: [Yes(Pizza, Lerato)]));

        Assert.Single(evaluation.Tallies);
        Assert.Equal(new RoundOutcome.Winner(Pizza), evaluation.Outcome);
    }

    [Fact]
    public void Surprise_me_only_proposes_eligible_options_and_honours_rejections()
    {
        var pool = new[] { Idea(Pizza), Idea(Bowling, "SUGGESTED"), Idea(Movie, "COMPLETED") };
        var random = new Random(7);

        for (var i = 0; i < 20; i++)
        {
            var pick = DecisionEngine.SurpriseMe(pool, random: random);
            Assert.NotNull(pick);
            Assert.NotEqual(Movie, pick!.Id);
        }

        var withoutPizza = DecisionEngine.SurpriseMe(pool, [Pizza], new Random(1));
        Assert.Equal(Bowling, withoutPizza!.Id);
        Assert.Null(DecisionEngine.SurpriseMe([Idea(Movie, "COMPLETED")]));
    }

    [Fact]
    public void Invite_codes_avoid_the_characters_people_misread()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = DecisionEngine.GenerateInviteCode();
            Assert.Equal(6, code.Length);
            Assert.False(code.Any(c => "IO01".Contains(c)));
            Assert.True(Vibe.Api.Security.Validators.IsInviteCode(code));
        }
    }
}
