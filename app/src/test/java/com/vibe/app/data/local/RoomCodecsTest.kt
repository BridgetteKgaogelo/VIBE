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
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * RoomDB stores lists in single text columns (list values separated by the ASCII
 * unit separator, tallies as `activityId:yes:no:pending`). Those codecs and the
 * entity mappers are the offline cache's contract, so they round-trip here on the
 * JVM - no emulator needed for the parts that can break silently.
 */
class RoomCodecsTest {

    @Test
    fun `a list survives the round trip to a single column`() {
        val photos = listOf("content://vibe/1.jpg", "content://vibe/2.jpg")
        assertEquals(photos, Codecs.decodeList(Codecs.encodeList(photos)))
    }

    @Test
    fun `empty and missing columns decode to an empty list`() {
        assertTrue(Codecs.decodeList(Codecs.encodeList(emptyList())).isEmpty())
        assertTrue(Codecs.decodeList(null).isEmpty())
        assertTrue(Codecs.decodeList("").isEmpty())
        // Trailing separators from an older row must not produce blank entries.
        assertTrue(Codecs.decodeList("\u001F").isEmpty())
    }

    @Test
    fun `tallies survive the round trip and damaged rows are skipped`() {
        val tallies = listOf(
            Tally(activityId = "pizza", yes = 3, no = 1, pending = 1),
            Tally(activityId = "bowling", yes = 0, no = 5, pending = 0),
        )
        assertEquals(tallies, Codecs.decodeTallies(Codecs.encodeTallies(tallies)))

        val damaged = Codecs.decodeTallies("pizza:3:1:1\u001Fbroken\u001Fbowling:x:1:1")
        assertEquals(1, damaged.size)
        assertEquals("pizza", damaged.single().activityId)
        assertTrue(Codecs.decodeTallies(null).isEmpty())
    }

    @Test
    fun `a user round trips through its entity`() {
        val user = User(
            id = "u-lerato",
            displayName = "Lerato",
            username = "lerato",
            email = "lerato@vibe.app",
            language = AppLanguage.SESOTHO,
            themeMode = ThemeMode.LIGHT,
            notificationsEnabled = false,
            privacyMembersOnly = true,
            photoUri = "content://vibe/me.jpg",
            createdAt = 1_700_000_000_000L,
        )
        assertEquals(user, user.toEntity().toDomain())
    }

    @Test
    fun `a group carries its members and role through Room`() {
        val group = Group(
            id = "g-friday",
            name = "Friday Night Crew",
            icon = "🎉",
            inviteCode = "FRYDAY",
            ownerId = "u-lerato",
            createdAt = 1_700_000_000_000L,
            role = GroupRole.OWNER,
            memberCount = 2,
            activityCount = 3,
            members = listOf(
                GroupMember("u-lerato", "Lerato", GroupRole.OWNER, null, 1L),
                GroupMember("u-thandi", "Thandi", GroupRole.MEMBER, "content://vibe/t.jpg", 2L),
            ),
            syncState = SyncState.PENDING,
        )

        val entity = group.toEntity(cachedAt = 42L)
        assertEquals(42L, entity.cachedAt)
        val restored = entity.toDomain(group.members.map { it.toEntity(group.id) })
        assertEquals(group.copy(members = group.members), restored)
    }

    @Test
    fun `an activity keeps its status, contributor and favourites`() {
        val activity = Activity(
            id = "act-1",
            groupId = "g-friday",
            title = "Bowling night",
            description = "Two games, loser buys the chips.",
            icon = "🎳",
            status = ActivityStatus.ACTIVE,
            createdBy = "u-sipho",
            createdByName = "Sipho",
            createdAt = 5L,
            favourite = true,
            yesVotes = 2,
            participantCount = 4,
            syncState = SyncState.CONFLICT,
        )
        assertEquals(activity, activity.toEntity().toDomain())
    }

    @Test
    fun `a decision keeps its tallies, survivors and the options of the round`() {
        val decision = Decision(
            id = "dec-1",
            groupId = "g-friday",
            roundNumber = 2,
            state = DecisionState.OPEN,
            deadlineEpochMillis = 1_700_000_600_000L,
            winnerActivityId = null,
            startedBy = "u-lerato",
            startedAt = 1_700_000_000_000L,
        )
        val entity = decision.toEntity(
            tallies = listOf(Tally("pizza", 3, 1, 0)),
            survivorIds = listOf("pizza", "bowling"),
            eliminatedIds = listOf("movie"),
            roundActivityIds = listOf("pizza", "bowling", "movie"),
        )

        assertEquals(decision, entity.toDomain())
        assertEquals(listOf(Tally("pizza", 3, 1, 0)), Codecs.decodeTallies(entity.talliesCsv))
        assertEquals(listOf("pizza", "bowling"), Codecs.decodeList(entity.survivorIdsCsv))
        assertEquals(listOf("movie"), Codecs.decodeList(entity.eliminatedIdsCsv))
        assertEquals(listOf("pizza", "bowling", "movie"), Codecs.decodeList(entity.roundActivityIdsCsv))
    }

    @Test
    fun `a vote keeps the member's choice and its offline state`() {
        val vote = Vote(
            id = "vote-1",
            decisionId = "dec-1",
            activityId = "pizza",
            userId = "u-thandi",
            choice = true,
            epochMillis = 1_700_000_123_456L,
            syncState = SyncState.PENDING,
        )
        assertEquals(vote, vote.toEntity().toDomain())
    }

    @Test
    fun `a plan and a memory keep their schedule, rating and photos`() {
        val plan = Plan(
            id = "plan-1",
            groupId = "g-friday",
            activityId = "act-1",
            activityTitle = "Bowling night",
            activityIcon = "🎳",
            scheduledAtEpochMillis = 1_700_000_000_000L,
            completed = true,
            createdBy = "u-lerato",
        )
        assertEquals(plan, plan.toEntity().toDomain())

        val memory = Memory(
            id = "mem-1",
            groupId = "g-friday",
            planId = plan.id,
            activityId = plan.activityId,
            activityTitle = plan.activityTitle,
            activityIcon = plan.activityIcon,
            completedAtEpochMillis = 1_700_000_500_000L,
            caption = "Naledi won by one pin.",
            rating = 5,
            photoUris = listOf("content://vibe/1.jpg", "content://vibe/2.jpg"),
            createdBy = "u-lerato",
            syncState = SyncState.SYNCED,
        )
        assertEquals(memory, memory.toEntity().toDomain())
    }

    @Test
    fun `an alert and a queued action keep their type and payload`() {
        val notification = VibeNotification(
            id = "n-1",
            userId = "u-lerato",
            title = "The group has a winner",
            body = "Open the plan to pick a date and time.",
            type = NotificationType.WINNER,
            read = true,
            createdAt = 9L,
            groupId = "g-friday",
        )
        assertEquals(notification, notification.toEntity().toDomain())

        val item = SyncItem(
            id = "activity.add:act-1:1234",
            action = SyncAction.ADD_ACTIVITY,
            entityId = "act-1",
            payloadJson = "{\"title\":\"Bowling night\"}",
            queuedAt = 1234L,
            state = SyncState.CONFLICT,
            attempts = 3,
            lastError = "changed on the server",
            serverPayloadJson = "{\"title\":\"Bowling\"}",
        )
        assertEquals(item, item.toEntity().toDomain())
        assertEquals("activity.add", item.toEntity().action)
    }

    @Test
    fun `an unknown enum value falls back instead of crashing the cache read`() {
        // A newer build could write a status this version does not know: reading
        // the cache must never throw, it must fall back and keep working.
        val unknown = Activity(id = "act-1", groupId = "g-friday", title = "Bowling night")
            .toEntity()
            .copy(status = "SOMETHING_NEW", syncState = "UNKNOWN")

        assertEquals(ActivityStatus.SUGGESTED, unknown.toDomain().status)
        assertEquals(SyncState.SYNCED, unknown.toDomain().syncState)

        val eliminated = unknown.copy(status = "ELIMINATED").toDomain()
        assertEquals(ActivityStatus.ELIMINATED, eliminated.status)
        assertFalse(eliminated.status.isEligibleForRounds)
    }
}
