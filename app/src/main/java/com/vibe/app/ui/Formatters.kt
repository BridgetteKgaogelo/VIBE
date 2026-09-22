package com.vibe.app.ui

import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Date and time formatting for plan dates, deadlines and memories.
 *
 * The app stores epoch milliseconds everywhere (cache, API, decision deadlines),
 * so a single formatting helper keeps every screen reading the same way, in the
 * member's locale.
 */
fun formatDateTime(epochMillis: Long): String =
    SimpleDateFormat("EEE d MMM, HH:mm", Locale.getDefault()).format(Date(epochMillis))

fun formatDate(epochMillis: Long): String =
    SimpleDateFormat("d MMM yyyy", Locale.getDefault()).format(Date(epochMillis))

fun formatDateWithTime(epochMillis: Long): String =
    SimpleDateFormat("d MMM yyyy, HH:mm", Locale.getDefault()).format(Date(epochMillis))
