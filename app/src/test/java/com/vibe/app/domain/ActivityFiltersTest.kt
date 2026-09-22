package com.vibe.app.domain

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The Vibe List filter pills are a pure function of the cached list, so they are
 * tested here rather than by tapping through the UI - the same list also has to
 * work offline, when the filter has nothing but RoomDB rows to look at.
 */
class ActivityFiltersTest {

    private val lerato = "u-lerato"
    private val thandi = "u-thandi"

    private fun activity(
        id: String,
        status: ActivityStatus = ActivityStatus.SUGGESTED,
        favourite: Boolean = false,
        createdBy: String = thandi,
        createdAt: Long = 0L,
    ) = Activity(
        id = id,
        groupId = "g-friday",
        title = id,
        status = status,
        favourite = favourite,
        createdBy = createdBy,
        createdAt = createdAt,
    )

    private val list = listOf(
        activity("pizza", createdAt = 30L),
        activity("bowling", status = ActivityStatus.ACTIVE, favourite = true, createdAt = 10L),
        activity("movie", status = ActivityStatus.COMPLETED, createdAt = 20L),
        activity("road trip", status = ActivityStatus.ELIMINATED, createdAt = 40L),
        activity("mine", createdBy = lerato, createdAt = 50L),
    )

    @Test
    fun `all shows everything on the list`() {
        assertEquals(5, ActivityFilters.apply(list, ActivityFilters.Option.ALL).size)
    }

    @Test
    fun `suggested keeps the ideas a round could still use`() {
        val ids = ActivityFilters.apply(list, ActivityFilters.Option.SUGGESTED).map { it.id }
        assertEquals(listOf("pizza", "bowling", "mine"), ids)
    }

    @Test
    fun `completed keeps only the plans that happened`() {
        assertEquals(listOf("movie"), ActivityFilters.apply(list, ActivityFilters.Option.COMPLETED).map { it.id })
    }

    @Test
    fun `favourites keeps only the starred ideas`() {
        assertEquals(listOf("bowling"), ActivityFilters.apply(list, ActivityFilters.Option.FAVOURITES).map { it.id })
    }

    @Test
    fun `mine needs a signed-in member and matches the contributor`() {
        assertEquals(listOf("mine"), ActivityFilters.apply(list, ActivityFilters.Option.MINE, lerato).map { it.id })
        assertTrue(ActivityFilters.apply(list, ActivityFilters.Option.MINE, null).isEmpty())
    }

    @Test
    fun `eligible drops everything that is decided or finished and sorts oldest first`() {
        val ids = ActivityFilters.eligible(list).map { it.id }
        assertEquals(listOf("bowling", "pizza", "mine"), ids)
    }

    @Test
    fun `an empty list stays empty for every filter`() {
        ActivityFilters.Option.entries.forEach { option ->
            assertTrue(ActivityFilters.apply(emptyList(), option, lerato).isEmpty())
        }
    }
}
