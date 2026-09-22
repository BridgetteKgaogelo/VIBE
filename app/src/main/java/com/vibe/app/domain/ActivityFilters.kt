package com.vibe.app.domain

/**
 * The filter pills on the Activities screen.
 *
 * Filtering is a pure function of the cached list, so it costs nothing offline and
 * is covered by `ActivityFiltersTest` rather than by tapping through the UI.
 */
object ActivityFilters {

    enum class Option { ALL, SUGGESTED, COMPLETED, FAVOURITES, MINE }

    fun apply(activities: List<Activity>, option: Option, userId: String? = null): List<Activity> =
        when (option) {
            Option.ALL -> activities
            Option.SUGGESTED -> activities.filter { it.status.isEligibleForRounds }
            Option.COMPLETED -> activities.filter { it.status == ActivityStatus.COMPLETED }
            Option.FAVOURITES -> activities.filter { it.favourite }
            Option.MINE -> activities.filter { userId != null && it.createdBy == userId }
        }

    /** The ideas a round can still use, newest last so the list reads chronologically. */
    fun eligible(activities: List<Activity>): List<Activity> =
        activities.filter { it.status.isEligibleForRounds }.sortedBy { it.createdAt }
}
