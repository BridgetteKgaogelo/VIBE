package com.vibe.app.core

import androidx.appcompat.app.AppCompatDelegate
import androidx.core.os.LocaleListCompat
import com.vibe.app.domain.AppLanguage

/**
 * Per-app language switching (PoE: the chosen language updates visible labels and
 * alerts).
 *
 * Android 13+ stores the choice for us through `android:localeConfig`; AppCompat
 * applies the same API down to API 24, so the app and its Firebase notifications
 * share one language setting.
 */
object LocaleManager {

    fun apply(language: AppLanguage) {
        val requested = LocaleListCompat.forLanguageTags(language.tag)
        if (AppCompatDelegate.getApplicationLocales() != requested) {
            AppCompatDelegate.setApplicationLocales(requested)
        }
    }
}
