package com.vibe.app.core

/**
 * Time is injected rather than read from `System.currentTimeMillis()` deep inside
 * the logic, so vote timestamps, deadlines and "round can close early" rules are
 * all testable without sleeping.
 */
fun interface TimeProvider {
    fun nowMillis(): Long
}

object SystemTime : TimeProvider {
    override fun nowMillis(): Long = System.currentTimeMillis()
}
