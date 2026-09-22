package com.vibe.app.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.vibe.app.core.VibeContainer

/**
 * The dependency container, provided once by [com.vibe.app.MainActivity] so every
 * screen and view model receives the same repositories, cache and sync manager.
 */
val LocalVibeContainer = staticCompositionLocalOf<VibeContainer> {
    error("VibeContainer was not provided: wrap the UI in a CompositionLocalProvider")
}

/**
 * Creates (or restores) a view model wired to the container.
 *
 * Keeping this in one place means no screen has to know how the repositories are
 * assembled, and it stays testable: a test can provide its own container.
 */
@Composable
inline fun <reified VM : ViewModel> vibeViewModel(crossinline build: (VibeContainer) -> VM): VM {
    val container = LocalVibeContainer.current
    return viewModel(
        factory = viewModelFactory {
            initializer { build(container) }
        },
    )
}
