package com.vibe.app.domain

/**
 * Domain models for VIBE.
 *
 * These types are deliberately free of Android, Retrofit and Room annotations so
 * the decision logic and the sync rules can be unit tested on the JVM (see
 * `app/src/test`). Timestamps are epoch milliseconds in UTC everywhere: from
 * RoomDB, over the REST API, to the UI formatter. Keeping one representation
 * removes a whole class of timezone bugs and keeps the offline queue comparable.
 */

enum class GroupRole { OWNER, MEMBER }

enum class ActivityStatus {
    /** On the Vibe List, waiting for a decision round. */
    SUGGESTED,

    /** Carried over into an open round. */
    ACTIVE,

    /** Dropped by the decision game because it could not progress. */
    ELIMINATED,

    /** Survived every round: the group's plan. */
    WINNER,

    /** The plan happened and is stored in Memories. */
    COMPLETED;

    /** Only these statuses may enter a decision round. */
    val isEligibleForRounds: Boolean get() = this == SUGGESTED || this == ACTIVE
}

enum class DecisionState { OPEN, CLOSED, SETTLED }

enum class SyncState { PENDING, SYNCED, CONFLICT, FAILED }

enum class ThemeMode { SYSTEM, DARK, LIGHT }

/** PoE: language choice drives both visible labels and alert copy. */
enum class AppLanguage(val tag: String, val label: String) {
    ENGLISH("en", "English"),
    ISIZULU("zu", "isiZulu"),
    SESOTHO("st", "Sesotho");

    companion object {
        fun fromTag(tag: String?): AppLanguage =
            entries.firstOrNull { it.tag.equals(tag, ignoreCase = true) } ?: ENGLISH
    }
}

enum class NotificationType { INVITE, NEW_ACTIVITY, DEADLINE, WINNER, MEMORY }

/** Queued offline actions. The wire name is what the REST API expects. */
enum class SyncAction(val wire: String) {
    CREATE_GROUP("group.create"),
    JOIN_GROUP("group.join"),
    LEAVE_GROUP("group.leave"),
    ADD_ACTIVITY("activity.add"),
    UPDATE_ACTIVITY("activity.update"),
    DELETE_ACTIVITY("activity.delete"),
    START_DECISION("decision.start"),
    CAST_VOTE("decision.vote"),
    CLOSE_DECISION("decision.close"),
    SAVE_PLAN("plan.save"),
    COMPLETE_PLAN("plan.complete"),
    SAVE_MEMORY("memory.save"),
    UPDATE_PROFILE("profile.update");

    companion object {
        fun fromWire(wire: String): SyncAction? = entries.firstOrNull { it.wire == wire }
    }
}

data class User(
    val id: String,
    val displayName: String,
    val username: String,
    val email: String,
    val language: AppLanguage = AppLanguage.ENGLISH,
    val themeMode: ThemeMode = ThemeMode.DARK,
    val notificationsEnabled: Boolean = true,
    val privacyMembersOnly: Boolean = true,
    val photoUri: String? = null,
    val createdAt: Long = 0L,
)

data class UserStats(val decisions: Int = 0, val activities: Int = 0, val groups: Int = 0)

data class GroupMember(
    val userId: String,
    val displayName: String,
    val role: GroupRole,
    val photoUri: String? = null,
    val joinedAt: Long = 0L,
)

data class Group(
    val id: String,
    val name: String,
    val icon: String,
    val inviteCode: String,
    val ownerId: String,
    val createdAt: Long,
    val role: GroupRole = GroupRole.MEMBER,
    val memberCount: Int = 1,
    val activityCount: Int = 0,
    val members: List<GroupMember> = emptyList(),
    val syncState: SyncState = SyncState.SYNCED,
)

data class Activity(
    val id: String,
    val groupId: String,
    val title: String,
    val description: String = "",
    val icon: String = "🎉",
    val status: ActivityStatus = ActivityStatus.SUGGESTED,
    val createdBy: String = "",
    val createdByName: String = "",
    val createdAt: Long = 0L,
    val favourite: Boolean = false,
    val yesVotes: Int = 0,
    val participantCount: Int = 0,
    val syncState: SyncState = SyncState.SYNCED,
) {
    fun toIdea() = ActivityIdea(
        id = id,
        title = title,
        icon = icon,
        status = status,
        createdAt = createdAt,
        createdBy = createdBy,
    )
}

data class Decision(
    val id: String,
    val groupId: String,
    val roundNumber: Int,
    val state: DecisionState,
    val deadlineEpochMillis: Long?,
    val winnerActivityId: String? = null,
    val startedBy: String = "",
    val startedAt: Long = 0L,
)

data class Vote(
    val id: String,
    val decisionId: String,
    val activityId: String,
    val userId: String,
    val choice: Boolean,
    val epochMillis: Long,
    val syncState: SyncState = SyncState.SYNCED,
)

data class Plan(
    val id: String,
    val groupId: String,
    val activityId: String,
    val activityTitle: String,
    val activityIcon: String,
    val scheduledAtEpochMillis: Long?,
    val completed: Boolean = false,
    val createdBy: String = "",
)

data class Memory(
    val id: String,
    val groupId: String,
    val planId: String,
    val activityId: String,
    val activityTitle: String,
    val activityIcon: String,
    val completedAtEpochMillis: Long,
    val caption: String = "",
    val rating: Int = 0,
    val photoUris: List<String> = emptyList(),
    val createdBy: String = "",
    val syncState: SyncState = SyncState.SYNCED,
)

data class VibeNotification(
    val id: String,
    val userId: String,
    val title: String,
    val body: String,
    val type: NotificationType,
    val read: Boolean = false,
    val createdAt: Long = 0L,
    val groupId: String? = null,
)

data class SyncItem(
    val id: String,
    val action: SyncAction,
    val entityId: String,
    val payloadJson: String,
    val queuedAt: Long,
    val state: SyncState = SyncState.PENDING,
    val attempts: Int = 0,
    val lastError: String? = null,
    val serverPayloadJson: String? = null,
)

/** A member's summary of one decision, used by the profile statistics. */
data class DecisionSummary(val decision: Decision, val activityTitle: String)

/**
 * Everything the decision screens need for one round: the decision itself, the
 * participation count, the tallies (only usable once [votesRevealed] is true)
 * and the outcome the engine reached.
 */
data class DecisionRound(
    val decision: Decision,
    val participation: Participation,
    val tallies: List<Tally> = emptyList(),
    val survivorIds: List<String> = emptyList(),
    val eliminatedIds: List<String> = emptyList(),
    val votesRevealed: Boolean = false,
    val canCloseEarly: Boolean = false,
    val outcome: RoundOutcome? = null,
) {
    val isOpen: Boolean get() = decision.state == DecisionState.OPEN
}
