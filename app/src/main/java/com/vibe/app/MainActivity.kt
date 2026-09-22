package com.vibe.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.lifecycleScope
import androidx.navigation.compose.rememberNavController
import com.vibe.app.core.LocaleManager
import com.vibe.app.core.VibeContainer
import com.vibe.app.ui.LocalVibeContainer
import com.vibe.app.ui.components.LoadingBox
import com.vibe.app.ui.navigation.Routes
import com.vibe.app.ui.navigation.VibeNavHost
import com.vibe.app.ui.theme.VibeTheme
import com.vibe.app.ui.theme.vibeIsDark
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch

/**
 * The single activity.
 *
 * Start destination follows the design document's flow: first launch lands on
 * onboarding, a signed-in member goes straight to Home, and a signed-out member
 * to Login. The theme and the language come from the settings store, so the
 * choice made in Profile applies immediately.
 */
class MainActivity : ComponentActivity() {

    private val container: VibeContainer by lazy { VibeContainer.provide(this) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        var startDestination by mutableStateOf<String?>(null)
        lifecycleScope.launch {
            val settings = container.settings.settings.first()
            val session = container.settings.session.first()
            LocaleManager.apply(settings.language)
            startDestination = when {
                !settings.onboarded -> Routes.ONBOARDING
                session == null -> Routes.LOGIN
                else -> Routes.HOME
            }
        }

        setContent {
            val settings by container.settings.settings.collectAsStateWithLifecycle(
                initialValue = com.vibe.app.data.prefs.AppSettings(),
            )

            LaunchedEffect(settings.language) { LocaleManager.apply(settings.language) }

            VibeTheme(themeMode = settings.themeMode) {
                CompositionLocalProvider(LocalVibeContainer provides container) {
                    val dark = vibeIsDark(settings.themeMode)
                    Box(
                        modifier = Modifier
                            .fillMaxSize()
                            .background(MaterialTheme.colorScheme.background),
                    ) {
                        val start = startDestination
                        if (start == null) {
                            LoadingBox()
                        } else {
                            // Re-created when the theme flips so the whole flow redraws.
                            val navController = rememberNavController()
                            val key = remember(dark) { dark.toString() }
                            androidx.compose.runtime.key(key) {
                                VibeNavHost(navController = navController, startDestination = start)
                            }
                        }
                    }
                }
            }
        }
    }
}
