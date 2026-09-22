@file:OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)

package com.vibe.app.data.repository

import com.vibe.app.core.TimeProvider
import com.vibe.app.core.VibeResult
import com.vibe.app.core.queuedOfflineResult
import com.vibe.app.core.toProblem
import com.vibe.app.data.local.Codecs
import com.vibe.app.data.local.DecisionEntity
import com.vibe.app.data.local.VibeDatabase
import com.vibe.app.data.local.VoteEntity
import com.vibe.app.data.local.toDomain
import com.vibe.app.data.local.toEntity
import com.vibe.app.data.prefs.SettingsStore
import com.vibe.app.data.remote.ApiErrorKind
import com.vibe.app.data.remote.ApiResult
import com.vibe.app.data.remote.CastVoteRequest
import com.vibe.app.data.remote.DecisionDto
import com.vibe.app.data.remote.StartDecisionRequest
import com.vibe.app.data.remote.SurpriseRequest
import com.vibe.app.data.remote.VibeApi
import com.vibe.app.data.remote.apiResult
import com.vibe.app.data.remote.toDomain
import com.vibe.app.data.remote.toRound
import com.vibe.app.data.sync.SyncQueue
import com.vibe.app.domain.Activity
import com.vibe.app.domain.ActivityStatus
import com.vibe.app.domain.DecisionEngine
import com.vibe.app.domain.DecisionRound
import com.vibe.app.domain.DecisionState
import com.vibe.app.domain.FieldError
import com.vibe.app.domain.GroupRole
import com.vibe.app.domain.Ids
import com.vibe.app.domain.RoundInput
import com.vibe.app.domain.RoundOutcome
import com.vibe.app.domain.RoundState
import com.vibe.app.domain.SyncAction
import com.vibe.app.domain.SyncState
import com.vibe.app.domain.Validators
import com.vibe.app.domain.VoteChoice
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.map

/**
 * Decide For Us: starting a round, voting, closing and the winner.
 *
 * The round the UI sees is always evaluated locally with [DecisionEngine] - the
 * very same rules the ASP.NET Core service applies - which is what makes an
 * offline vote visible immediately while still hiding the score until the round
 * closes. Every vote is written to RoomDB with the moment it was cast, queued as
 * a `decision.vote` action, and then sent to the API.
 */
class DecisionRepository(
    private val api: VibeApi,
    private val database: VibeDatabase,
    private val queue: SyncQueue,
    private val settings: SettingsStore,
    private val time: TimeProvider,
) {

    fun observeRound(decisionId: String): Flow<DecisionRound?> = combine(
        database.decisionDao().observeById(decisionId),
        database.voteDao().observeByDecision(decisionId),
        database.activityDao().observeAll(),
    ) { entity, votes, activities -> Triple(entity, votes, activities) }
        .flatMapLatest { (entity, votes, activities) ->
            flow {
                emit(entity?.let { buildRound(it, votes, activities) })
            }
        }

    /** The round a group is currently playing, if any. */
    fun observeCurrentRound(groupId: String): Flow<DecisionRound?> =
        database.decisionDao().observeLatestOpen(groupId).flatMapLatest { entity ->
            if (entity == null) {
                flow { emit(null) }
            } else {
                observeRound(entity.id)
            }
        }

    /** The most recent settled round, used by the winner screen. */
    fun observeLatestWinner(groupId: String): Flow<DecisionRound?> =
        database.decisionDao().observeLatestSettled(groupId).flatMapLatest { entity ->
            if (entity == null) {
                flow { emit(null) }
            } else {
                observeRound(entity.id)
            }
        }

    fun observeHistory(groupId: String): Flow<List<com.vibe.app.domain.Decision>> =
        database.decisionDao().observeByGroup(groupId).map { rows -> rows.map { it.toDomain() } }

    suspend fun startRound(groupId: String, activityIds: List<String> = emptyList()): VibeResult<String> {
        val userId = settings.currentUserId() ?: return VibeResult.Problem(detail = "Not signed in")
        val group = database.groupDao().byId(groupId) ?: return VibeResult.Problem(detail = "Group not found")
        val membership = database.groupDao().members(groupId).firstOrNull { it.userId == userId }
        val isOwner = group.ownerId == userId || membership?.role == GroupRole.OWNER.name
        Validators.canStartRound(if (isOwner) GroupRole.OWNER else GroupRole.MEMBER)
            ?.let { return VibeResult.Problem(error = it) }

        val eligible = database.activityDao().eligibleForRound(groupId)
            .filter { activityIds.isEmpty() || it.id in activityIds }
        Validators.roundActivities(eligible.size)?.let { return VibeResult.Problem(error = it) }

        val requestedIds = if (activityIds.isEmpty()) eligible.map { it.id } else activityIds
        val clientId = Ids.newId()
        val payload = StartDecisionRequest(activityIds = requestedIds, clientId = clientId, groupId = groupId)

        return when (val result = apiResult { api.startDecision(groupId, payload) }) {
            is ApiResult.Success -> {
                val dto = result.data
                applySnapshot(dto)
                VibeResult.Ok(dto.id)
            }

            is ApiResult.Failure -> if (result.kind == ApiErrorKind.NETWORK) {
                queue.enqueue(SyncAction.START_DECISION, clientId, payload, StartDecisionRequest::class.java)
                VibeResult.Problem(error = null, kind = ApiErrorKind.NETWORK, queuedOffline = true)
            } else {
                result.toProblem()
            }
        }
    }

    /**
     * One YES/NO vote for one activity. The vote is recorded locally first, with
     * the client timestamp, so a queued vote keeps the time the member tapped it.
     */
    suspend fun castVote(decisionId: String, activityId: String, choice: Boolean): VibeResult<String> {
        val userId = settings.currentUserId() ?: return VibeResult.Problem(detail = "Not signed in")
        val decision = database.decisionDao().byId(decisionId) ?: return VibeResult.Problem(detail = "Round not found")
        if (decision.state != DecisionState.OPEN.name) {
            return VibeResult.Problem(kind = ApiErrorKind.CONFLICT, detail = "This round is already closed.")
        }

        val now = time.nowMillis()
        val clientId = Ids.syncClientId(SyncAction.CAST_VOTE, "$decisionId:$activityId", now)
        val vote = VoteEntity(
            decisionId = decisionId,
            activityId = activityId,
            userId = userId,
            voteId = clientId,
            choice = choice,
            epochMillis = now,
            syncState = SyncState.PENDING.name,
        )
        database.voteDao().upsert(vote)

        val payload = CastVoteRequest(
            activityId = activityId,
            choice = choice,
            clientTimestampEpochMillis = now,
            clientId = clientId,
        )
        queue.enqueue(SyncAction.CAST_VOTE, "$decisionId:$activityId", payload, CastVoteRequest::class.java)

        return when (val result = apiResult { api.castVote(decisionId, payload) }) {
            is ApiResult.Success -> {
                database.voteDao().updateSyncState(decisionId, activityId, userId, SyncState.SYNCED.name)
                applySnapshot(result.data.decision)
                VibeResult.Ok(result.data.decision.id)
            }

            is ApiResult.Failure -> if (result.kind == ApiErrorKind.NETWORK) {
                // The vote is safe on the device and shows in the local round.
                VibeResult.Ok(decisionId)
            } else {
                result.toProblem()
            }
        }
    }

    suspend fun closeRound(decisionId: String): VibeResult<String> =
        when (val result = apiResult { api.closeDecision(decisionId) }) {
            is ApiResult.Success -> {
                applySnapshot(result.data)
                VibeResult.Ok(result.data.id)
            }

            is ApiResult.Failure -> result.toProblem()
        }

    suspend fun refreshRound(decisionId: String): VibeResult<DecisionRound> =
        when (val result = apiResult { api.decision(decisionId) }) {
            is ApiResult.Success -> {
                applySnapshot(result.data)
                VibeResult.Ok(result.data.toRound())
            }

            is ApiResult.Failure -> result.toProblem()
        }

    /** PoE: "Surprise Me" randomly proposes one eligible activity. */
    suspend fun surprise(decisionId: String, rejectedIds: List<String>): VibeResult<Activity> =
        when (val result = apiResult { api.surprise(decisionId, SurpriseRequest(rejectedIds)) }) {
            is ApiResult.Success -> VibeResult.Ok(result.data.toDomain())
            is ApiResult.Failure -> result.toProblem()
        }

    suspend fun queueOfflineRoundStart(groupId: String, activityIds: List<String>): VibeResult<Unit> {
        val clientId = Ids.newId()
        val payload = StartDecisionRequest(activityIds = activityIds, clientId = clientId, groupId = groupId)
        queue.enqueue(SyncAction.START_DECISION, "$groupId:$clientId", payload, StartDecisionRequest::class.java)
        return queuedOfflineResult()
    }

    // ------------------------------------------------------------ internals

    /**
     * Rebuilds the round from cached rows. This is the offline truth: the same
     * inputs and the same engine the server uses.
     */
    private suspend fun buildRound(
        entity: DecisionEntity,
        votes: List<VoteEntity>,
        activities: List<com.vibe.app.data.local.ActivityEntity>,
    ): DecisionRound {
        val members = database.groupDao().members(entity.groupId).map { it.userId }
        val roundIds = Codecs.decodeList(entity.roundActivityIdsCsv)
        val ideas = activities
            .filter { it.id in roundIds }
            .map { it.toDomain().toIdea() }

        val evaluation = DecisionEngine.evaluate(
            RoundInput(
                roundNumber = entity.roundNumber,
                state = when (entity.state) {
                    DecisionState.OPEN.name -> RoundState.OPEN
                    DecisionState.CLOSED.name -> RoundState.CLOSED
                    else -> RoundState.SETTLED
                },
                nowEpochMillis = time.nowMillis(),
                memberIds = members,
                activities = ideas,
                votes = votes.map { VoteChoice(it.activityId, it.userId, it.choice, it.epochMillis) },
                deadlineEpochMillis = entity.deadlineEpochMillis,
            ),
        )

        return DecisionRound(
            decision = entity.toDomain(),
            participation = evaluation.participation,
            tallies = if (evaluation.votesRevealed) evaluation.tallies else emptyList(),
            survivorIds = if (evaluation.votesRevealed) evaluation.survivorIds else Codecs.decodeList(entity.survivorIdsCsv),
            eliminatedIds = if (evaluation.votesRevealed) evaluation.eliminatedIds else emptyList(),
            votesRevealed = evaluation.votesRevealed,
            canCloseEarly = evaluation.canCloseEarly,
            outcome = evaluation.outcome,
        )
    }

    /** Writes a server snapshot into the cache and keeps the Vibe List statuses in step. */
    private suspend fun applySnapshot(dto: DecisionDto) {
        val existing = database.decisionDao().byId(dto.id)
        val roundActivityIds = existing?.let { Codecs.decodeList(it.roundActivityIdsCsv) }
            ?: (dto.survivorIds + dto.eliminatedIds).distinct()

        database.decisionDao().upsert(
            dto.toDomain().toEntity(
                tallies = dto.tallies.map { it.toDomain() },
                survivorIds = dto.survivorIds,
                eliminatedIds = dto.eliminatedIds,
                roundActivityIds = roundActivityIds,
            ),
        )

        dto.eliminatedIds.forEach { database.activityDao().updateStatus(it, ActivityStatus.ELIMINATED.name) }
        dto.winnerActivityId?.let { database.activityDao().updateStatus(it, ActivityStatus.WINNER.name) }
        if (dto.state == DecisionState.CLOSED.name || dto.outcome == "continue") {
            dto.survivorIds.forEach { database.activityDao().updateStatus(it, ActivityStatus.ACTIVE.name) }
        }
    }

    /** Set by the UI so the round row can be refreshed while voting is open. */
    fun isRoundDecided(round: DecisionRound): Boolean =
        round.outcome is RoundOutcome.Winner ||
            round.outcome is RoundOutcome.Tie ||
            round.outcome is RoundOutcome.NoWinner
}
