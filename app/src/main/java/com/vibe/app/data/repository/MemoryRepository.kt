package com.vibe.app.data.repository

import com.vibe.app.core.TimeProvider
import com.vibe.app.core.VibeResult
import com.vibe.app.core.toProblem
import com.vibe.app.data.local.VibeDatabase
import com.vibe.app.data.local.toDomain
import com.vibe.app.data.local.toEntity
import com.vibe.app.data.prefs.SettingsStore
import com.vibe.app.data.remote.ApiErrorKind
import com.vibe.app.data.remote.ApiResult
import com.vibe.app.data.remote.CompletePlanRequest
import com.vibe.app.data.remote.SavePlanRequest
import com.vibe.app.data.remote.VibeApi
import com.vibe.app.data.remote.apiResult
import com.vibe.app.data.remote.toDomain
import com.vibe.app.data.sync.SyncQueue
import com.vibe.app.domain.ActivityStatus
import com.vibe.app.domain.Ids
import com.vibe.app.domain.Memory
import com.vibe.app.domain.Plan
import com.vibe.app.domain.SyncAction
import com.vibe.app.domain.SyncState
import com.vibe.app.domain.Validators
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

/**
 * Plans and Memories: setting the date, marking a plan done, and the photos,
 * caption and group rating that make the memory.
 *
 * Photos stay on the device in the PoE build (the URI is stored with the memory
 * and rendered with Coil). A deployed API accepts uploaded URLs for the same
 * field, so switching to Azure Blob Storage later needs no schema change.
 */
class MemoryRepository(
    private val api: VibeApi,
    private val database: VibeDatabase,
    private val queue: SyncQueue,
    private val settings: SettingsStore,
    private val time: TimeProvider,
) {

    val memories: Flow<List<Memory>> =
        database.memoryDao().observeAll().map { rows -> rows.map { it.toDomain() } }

    fun observeForGroup(groupId: String): Flow<List<Memory>> =
        database.memoryDao().observeByGroup(groupId).map { rows -> rows.map { it.toDomain() } }

    fun observeMemory(memoryId: String): Flow<Memory?> =
        database.memoryDao().observeById(memoryId).map { it?.toDomain() }

    fun observePlans(groupId: String): Flow<List<Plan>> =
        database.planDao().observeByGroup(groupId).map { rows -> rows.map { it.toDomain() } }

    suspend fun refreshMemories(groupId: String): VibeResult<Int> =
        when (val result = apiResult { api.memories(groupId) }) {
            is ApiResult.Success -> {
                database.memoryDao().upsert(result.data.map { it.toDomain().toEntity() })
                VibeResult.Ok(result.data.size)
            }

            is ApiResult.Failure -> result.toProblem()
        }

    suspend fun refreshPlans(groupId: String): VibeResult<Int> =
        when (val result = apiResult { api.plans(groupId) }) {
            is ApiResult.Success -> {
                result.data.forEach { database.planDao().upsert(it.toDomain().toEntity()) }
                VibeResult.Ok(result.data.size)
            }

            is ApiResult.Failure -> result.toProblem()
        }

    /** "Plan it": the winner gets a date and time. */
    suspend fun savePlan(groupId: String, activityId: String, scheduledAtEpochMillis: Long?): VibeResult<Plan> {
        val userId = settings.currentUserId() ?: return VibeResult.Problem(detail = "Not signed in")
        val activity = database.activityDao().byId(activityId)
            ?: return VibeResult.Problem(detail = "Activity not found")
        val existing = database.planDao().latestForActivity(activityId)

        val plan = Plan(
            id = existing?.id ?: Ids.newId(),
            groupId = groupId,
            activityId = activityId,
            activityTitle = activity.title,
            activityIcon = activity.icon,
            scheduledAtEpochMillis = scheduledAtEpochMillis,
            completed = existing?.completed ?: false,
            createdBy = userId,
        )
        database.planDao().upsert(plan.toEntity())

        val payload = SavePlanRequest(groupId, activityId, scheduledAtEpochMillis)
        queue.enqueue(SyncAction.SAVE_PLAN, plan.id, payload, SavePlanRequest::class.java)

        return when (val result = apiResult { api.savePlan(payload) }) {
            is ApiResult.Success -> {
                val saved = result.data.toDomain()
                database.planDao().upsert(saved.toEntity())
                VibeResult.Ok(saved)
            }

            is ApiResult.Failure -> VibeResult.Ok(plan)
        }
    }

    /** "Mark as done" plus caption, rating and photos: this creates the memory. */
    suspend fun completePlan(
        planId: String,
        rating: Int,
        caption: String,
        photoUris: List<String> = emptyList(),
    ): VibeResult<Memory> {
        Validators.rating(rating)?.let { return VibeResult.Problem(error = it) }
        Validators.caption(caption)?.let { return VibeResult.Problem(error = it) }
        val userId = settings.currentUserId() ?: return VibeResult.Problem(detail = "Not signed in")
        val plan = database.planDao().byId(planId) ?: return VibeResult.Problem(detail = "Plan not found")

        val now = time.nowMillis()
        val memory = Memory(
            id = Ids.newId(),
            groupId = plan.groupId,
            planId = plan.id,
            activityId = plan.activityId,
            activityTitle = plan.activityTitle,
            activityIcon = plan.activityIcon,
            completedAtEpochMillis = now,
            caption = caption.trim(),
            rating = rating,
            photoUris = photoUris,
            createdBy = userId,
            syncState = SyncState.PENDING,
        )

        database.planDao().upsert(plan.copy(completed = true))
        database.activityDao().updateStatus(plan.activityId, ActivityStatus.COMPLETED.name)
        database.memoryDao().upsert(memory.toEntity())

        val payload = CompletePlanRequest(
            rating = rating,
            caption = memory.caption,
            photoUris = photoUris,
            planId = planId,
            groupId = memory.groupId,
        )
        queue.enqueue(SyncAction.COMPLETE_PLAN, planId, payload, CompletePlanRequest::class.java)

        return when (val result = apiResult { api.completePlan(planId, payload) }) {
            is ApiResult.Success -> {
                val saved = result.data.toDomain()
                database.memoryDao().upsert(saved.toEntity())
                VibeResult.Ok(saved)
            }

            is ApiResult.Failure -> if (result.kind == ApiErrorKind.NETWORK) {
                VibeResult.Ok(memory)
            } else {
                result.toProblem()
            }
        }
    }

    /** Adds a photo to an existing memory (device URI in the PoE build). */
    suspend fun addPhoto(memoryId: String, photoUri: String): VibeResult<Memory> {
        val entity = database.memoryDao().byId(memoryId) ?: return VibeResult.Problem(detail = "Memory not found")
        val memory = entity.toDomain()
        val updated = memory.copy(photoUris = memory.photoUris + photoUri, syncState = SyncState.PENDING)
        database.memoryDao().upsert(updated.toEntity())
        return VibeResult.Ok(updated)
    }

    /** Edits an existing memory (caption, rating, photos). */
    suspend fun updateMemory(
        memoryId: String,
        rating: Int,
        caption: String,
        photoUris: List<String>,
    ): VibeResult<Memory> {
        Validators.rating(rating)?.let { return VibeResult.Problem(error = it) }
        Validators.caption(caption)?.let { return VibeResult.Problem(error = it) }
        val entity = database.memoryDao().byId(memoryId) ?: return VibeResult.Problem(detail = "Memory not found")
        val updated = entity.toDomain().copy(
            rating = rating,
            caption = caption.trim(),
            photoUris = photoUris,
            syncState = SyncState.PENDING,
        )
        database.memoryDao().upsert(updated.toEntity())

        val payload = CompletePlanRequest(
            rating = rating,
            caption = updated.caption,
            photoUris = photoUris,
            planId = entity.planId,
            groupId = updated.groupId,
        )
        queue.enqueue(SyncAction.SAVE_MEMORY, memoryId, payload, CompletePlanRequest::class.java)
        return when (val result = apiResult { api.completePlan(entity.planId, payload) }) {
            is ApiResult.Success -> {
                val saved = result.data.toDomain()
                database.memoryDao().upsert(saved.toEntity())
                VibeResult.Ok(saved)
            }

            is ApiResult.Failure -> VibeResult.Ok(updated)
        }
    }

    suspend fun memoryCount(): Int = database.memoryDao().count()
}
