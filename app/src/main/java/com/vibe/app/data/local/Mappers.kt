package com.vibe.app.data.local

import com.vibe.app.domain.Activity
import com.vibe.app.domain.ActivityStatus
import com.vibe.app.domain.AppLanguage
import com.vibe.app.domain.Decision
import com.vibe.app.domain.DecisionState
import com.vibe.app.domain.Group
import com.vibe.app.domain.GroupMember
import com.vibe.app.domain.GroupRole
import com.vibe.app.domain.Memory
import com.vibe.app.domain.NotificationType
import com.vibe.app.domain.Plan
import com.vibe.app.domain.SyncAction
import com.vibe.app.domain.SyncItem
import com.vibe.app.domain.SyncState
import com.vibe.app.domain.Tally
import com.vibe.app.domain.ThemeMode
import com.vibe.app.domain.User
import com.vibe.app.domain.VibeNotification
import com.vibe.app.domain.Vote

/**
 * Room <-> domain mapping plus the compact codecs used for list columns.
 *
 * Lists are stored as single text columns separated by the ASCII unit separator
 * (\u001F) and tallies as `activityId:yes:no:pending`. Both codecs round-trip in
 * unit tests (see `RoomCodecsTest`), which keeps the offline cache cheap to read
 * without pulling a JSON parser into the database layer.
 */

private const val SEP = "\u001F"

object Codecs {

    fun encodeList(values: List<String>): String = values.joinToString(SEP)

    fun decodeList(value: String?): List<String> =
        if (value.isNullOrBlank()) emptyList() else value.split(SEP).filter { it.isNotBlank() }

    fun encodeTallies(tallies: List<Tally>): String =
        tallies.joinToString(SEP) { "${it.activityId}:${it.yes}:${it.no}:${it.pending}" }

    fun decodeTallies(value: String?): List<Tally> {
        if (value.isNullOrBlank()) return emptyList()
        return value.split(SEP).mapNotNull { part ->
            val bits = part.split(":")
            if (bits.size != 4) return@mapNotNull null
            val yes = bits[1].toIntOrNull() ?: return@mapNotNull null
            val no = bits[2].toIntOrNull() ?: return@mapNotNull null
            val pending = bits[3].toIntOrNull() ?: return@mapNotNull null
            Tally(activityId = bits[0], yes = yes, no = no, pending = pending)
        }
    }
}

internal inline fun <reified T : Enum<T>> enumOr(raw: String?, fallback: T): T =
    enumValues<T>().firstOrNull { it.name.equals(raw, ignoreCase = true) } ?: fallback

fun UserEntity.toDomain() = User(
    id = id,
    displayName = displayName,
    username = username,
    email = email,
    language = AppLanguage.fromTag(language),
    themeMode = enumOr(themeMode, ThemeMode.DARK),
    notificationsEnabled = notificationsEnabled,
    privacyMembersOnly = privacyMembersOnly,
    photoUri = photoUri,
    createdAt = createdAt,
)

fun User.toEntity() = UserEntity(
    id = id,
    displayName = displayName,
    username = username,
    email = email,
    language = language.tag,
    themeMode = themeMode.name,
    notificationsEnabled = notificationsEnabled,
    privacyMembersOnly = privacyMembersOnly,
    photoUri = photoUri,
    createdAt = createdAt,
)

fun GroupEntity.toDomain(members: List<GroupMemberEntity> = emptyList()) = Group(
    id = id,
    name = name,
    icon = icon,
    inviteCode = inviteCode,
    ownerId = ownerId,
    createdAt = createdAt,
    role = enumOr(role, GroupRole.MEMBER),
    memberCount = memberCount,
    activityCount = activityCount,
    members = members.map { it.toDomain() },
    syncState = enumOr(syncState, SyncState.SYNCED),
)

fun Group.toEntity(cachedAt: Long) = GroupEntity(
    id = id,
    name = name,
    icon = icon,
    inviteCode = inviteCode,
    ownerId = ownerId,
    createdAt = createdAt,
    role = role.name,
    memberCount = memberCount,
    activityCount = activityCount,
    syncState = syncState.name,
    cachedAt = cachedAt,
)

fun GroupMemberEntity.toDomain() = GroupMember(
    userId = userId,
    displayName = displayName,
    role = enumOr(role, GroupRole.MEMBER),
    photoUri = photoUri,
    joinedAt = joinedAt,
)

fun GroupMember.toEntity(groupId: String) = GroupMemberEntity(
    groupId = groupId,
    userId = userId,
    displayName = displayName,
    role = role.name,
    photoUri = photoUri,
    joinedAt = joinedAt,
)

fun ActivityEntity.toDomain() = Activity(
    id = id,
    groupId = groupId,
    title = title,
    description = description,
    icon = icon,
    status = enumOr(status, ActivityStatus.SUGGESTED),
    createdBy = createdBy,
    createdByName = createdByName,
    createdAt = createdAt,
    favourite = favourite,
    yesVotes = yesVotes,
    participantCount = participantCount,
    syncState = enumOr(syncState, SyncState.SYNCED),
)

fun Activity.toEntity() = ActivityEntity(
    id = id,
    groupId = groupId,
    title = title,
    description = description,
    icon = icon,
    status = status.name,
    createdBy = createdBy,
    createdByName = createdByName,
    createdAt = createdAt,
    favourite = favourite,
    yesVotes = yesVotes,
    participantCount = participantCount,
    syncState = syncState.name,
)

fun DecisionEntity.toDomain() = Decision(
    id = id,
    groupId = groupId,
    roundNumber = roundNumber,
    state = enumOr(state, DecisionState.OPEN),
    deadlineEpochMillis = deadlineEpochMillis,
    winnerActivityId = winnerActivityId,
    startedBy = startedBy,
    startedAt = startedAt,
)

fun Decision.toEntity(
    tallies: List<Tally> = emptyList(),
    survivorIds: List<String> = emptyList(),
    eliminatedIds: List<String> = emptyList(),
    roundActivityIds: List<String> = emptyList(),
) = DecisionEntity(
    id = id,
    groupId = groupId,
    roundNumber = roundNumber,
    state = state.name,
    deadlineEpochMillis = deadlineEpochMillis,
    winnerActivityId = winnerActivityId,
    startedBy = startedBy,
    startedAt = startedAt,
    talliesCsv = Codecs.encodeTallies(tallies),
    survivorIdsCsv = Codecs.encodeList(survivorIds),
    eliminatedIdsCsv = Codecs.encodeList(eliminatedIds),
    roundActivityIdsCsv = Codecs.encodeList(roundActivityIds),
)

fun VoteEntity.toDomain() = Vote(
    id = voteId,
    decisionId = decisionId,
    activityId = activityId,
    userId = userId,
    choice = choice,
    epochMillis = epochMillis,
    syncState = enumOr(syncState, SyncState.SYNCED),
)

fun Vote.toEntity() = VoteEntity(
    decisionId = decisionId,
    activityId = activityId,
    userId = userId,
    voteId = id,
    choice = choice,
    epochMillis = epochMillis,
    syncState = syncState.name,
)

fun PlanEntity.toDomain() = Plan(
    id = id,
    groupId = groupId,
    activityId = activityId,
    activityTitle = activityTitle,
    activityIcon = activityIcon,
    scheduledAtEpochMillis = scheduledAtEpochMillis,
    completed = completed,
    createdBy = createdBy,
)

fun Plan.toEntity() = PlanEntity(
    id = id,
    groupId = groupId,
    activityId = activityId,
    activityTitle = activityTitle,
    activityIcon = activityIcon,
    scheduledAtEpochMillis = scheduledAtEpochMillis,
    completed = completed,
    createdBy = createdBy,
)

fun MemoryEntity.toDomain() = Memory(
    id = id,
    groupId = groupId,
    planId = planId,
    activityId = activityId,
    activityTitle = activityTitle,
    activityIcon = activityIcon,
    completedAtEpochMillis = completedAtEpochMillis,
    caption = caption,
    rating = rating,
    photoUris = Codecs.decodeList(photoUrisCsv),
    createdBy = createdBy,
    syncState = enumOr(syncState, SyncState.SYNCED),
)

fun Memory.toEntity() = MemoryEntity(
    id = id,
    groupId = groupId,
    planId = planId,
    activityId = activityId,
    activityTitle = activityTitle,
    activityIcon = activityIcon,
    completedAtEpochMillis = completedAtEpochMillis,
    caption = caption,
    rating = rating,
    photoUrisCsv = Codecs.encodeList(photoUris),
    createdBy = createdBy,
    syncState = syncState.name,
)

fun NotificationEntity.toDomain() = VibeNotification(
    id = id,
    userId = userId,
    title = title,
    body = body,
    type = enumOr(type, NotificationType.NEW_ACTIVITY),
    read = read,
    createdAt = createdAt,
    groupId = groupId,
)

fun VibeNotification.toEntity() = NotificationEntity(
    id = id,
    userId = userId,
    title = title,
    body = body,
    type = type.name,
    read = read,
    createdAt = createdAt,
    groupId = groupId,
)

fun SyncItemEntity.toDomain() = SyncItem(
    id = id,
    action = SyncAction.fromWire(action) ?: SyncAction.UPDATE_ACTIVITY,
    entityId = entityId,
    payloadJson = payloadJson,
    queuedAt = queuedAt,
    state = enumOr(state, SyncState.PENDING),
    attempts = attempts,
    lastError = lastError,
    serverPayloadJson = serverPayloadJson,
)

fun SyncItem.toEntity() = SyncItemEntity(
    id = id,
    action = action.wire,
    entityId = entityId,
    payloadJson = payloadJson,
    queuedAt = queuedAt,
    state = state.name,
    attempts = attempts,
    lastError = lastError,
    serverPayloadJson = serverPayloadJson,
)
