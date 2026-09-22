namespace Vibe.Api.Domain;

public enum RoundState
{
    Open,
    Closed,
    Settled,
}

/// <summary>One option in a round: exactly what the decision game needs to know.</summary>
public sealed record ActivityIdea(
    Guid Id,
    string Title,
    string Icon = "🎉",
    string Status = "SUGGESTED",
    long CreatedAtEpochMillis = 0L);

public sealed record VoteChoice(Guid ActivityId, Guid UserId, bool Choice, long CastAtEpochMillis = 0L);

public sealed record RoundInput(
    int RoundNumber,
    RoundState State,
    long NowEpochMillis,
    IReadOnlyList<Guid> MemberIds,
    IReadOnlyList<ActivityIdea> Activities,
    IReadOnlyList<VoteChoice> Votes,
    long? DeadlineEpochMillis = null,
    int MaxRounds = DecisionEngine.MaxRounds);

public sealed record Tally(Guid ActivityId, int Yes, int No, int Pending)
{
    public int Cast => Yes + No;

    /// <summary>More YES than NO: this option stays in the running.</summary>
    public bool CanProgress => Yes > No;

    /// <summary>Could still win once every pending member has voted.</summary>
    public bool CouldStillWin => Yes + Pending > No;

    public double ApprovalRatio => Cast == 0 ? 0d : (double)Yes / Cast;
}

public sealed record Participation(int MemberCount, int VotedCount, IReadOnlyList<Guid> PendingMemberIds)
{
    public bool EveryoneVoted => MemberCount > 0 && VotedCount >= MemberCount;

    public float Progress => MemberCount == 0 ? 0f : (float)VotedCount / MemberCount;
}

public abstract record RoundOutcome
{
    public sealed record InProgress(IReadOnlyList<Guid> WaitingForMemberIds) : RoundOutcome;

    public sealed record Winner(Guid ActivityId) : RoundOutcome;

    public sealed record ContinueRounds(IReadOnlyList<Guid> SurvivorIds, int NextRoundNumber) : RoundOutcome;

    public sealed record Tie(IReadOnlyList<Guid> SurvivorIds) : RoundOutcome;

    public sealed record NoWinner : RoundOutcome;

    /// <summary>The wire value the client reads: inProgress | winner | continue | tie | none.</summary>
    public string Wire => this switch
    {
        InProgress => "inProgress",
        Winner => "winner",
        ContinueRounds => "continue",
        Tie => "tie",
        _ => "none",
    };
}

public sealed record RoundEvaluation(
    int RoundNumber,
    RoundOutcome Outcome,
    IReadOnlyList<Tally> Tallies,
    IReadOnlyList<ActivityIdea> Survivors,
    IReadOnlyList<ActivityIdea> Eliminated,
    Participation Participation,
    bool VotesRevealed,
    bool CanCloseEarly)
{
    public IReadOnlyList<Guid> SurvivorIds => Survivors.Select(a => a.Id).ToList();

    public IReadOnlyList<Guid> EliminatedIds => Eliminated.Select(a => a.Id).ToList();
}
