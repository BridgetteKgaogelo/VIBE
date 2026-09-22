package com.vibe.app.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.vibe.app.domain.ThemeMode

/**
 * The VIBE visual system (design document, section 4).
 *
 * Midnight navy is the canvas, coral is reserved for the primary action, mint
 * means confirmed/YES, and lilac carries secondary information. Cards use 20 dp
 * rounded corners and the type is bold and friendly. Dark is the default; the
 * light theme swaps the navy canvas for a blush one so the same accents keep
 * their contrast ratio.
 */
val VibeNavy = Color(0xFF0B1026)
val VibeNavyElevated = Color(0xFF141B3D)
val VibeNavySoft = Color(0xFF1E2749)
val VibeCoral = Color(0xFFFF6B6B)
val VibeCoralPressed = Color(0xFFE85555)
val VibeLilac = Color(0xFFB79CFF)
val VibeMint = Color(0xFF4FD1A5)
val VibeInk = Color(0xFF14162B)
val VibeInkMuted = Color(0xFF6B7090)
val VibeBlush = Color(0xFFFFF4F8)
val VibeWhite = Color(0xFFFFFFFF)
val VibeTextOnDark = Color(0xFFF4F6FF)
val VibeTextMutedDark = Color(0xFFA6ADD1)

private val DarkScheme = darkColorScheme(
    primary = VibeCoral,
    onPrimary = VibeWhite,
    primaryContainer = Color(0xFF4A2036),
    onPrimaryContainer = VibeWhite,
    secondary = VibeLilac,
    onSecondary = VibeInk,
    secondaryContainer = VibeNavySoft,
    onSecondaryContainer = VibeTextOnDark,
    tertiary = VibeMint,
    onTertiary = VibeInk,
    background = VibeNavy,
    onBackground = VibeTextOnDark,
    surface = VibeNavyElevated,
    onSurface = VibeTextOnDark,
    surfaceVariant = VibeNavySoft,
    onSurfaceVariant = VibeTextMutedDark,
    outline = Color(0xFF3A4374),
    error = Color(0xFFFF8A80),
    onError = VibeInk,
)

private val LightScheme = lightColorScheme(
    primary = VibeCoral,
    onPrimary = VibeWhite,
    primaryContainer = Color(0xFFFFE0DD),
    onPrimaryContainer = VibeInk,
    secondary = Color(0xFF7B5CD6),
    onSecondary = VibeWhite,
    secondaryContainer = Color(0xFFEDE7FF),
    onSecondaryContainer = VibeInk,
    tertiary = Color(0xFF1E9E74),
    onTertiary = VibeWhite,
    background = VibeBlush,
    onBackground = VibeInk,
    surface = VibeWhite,
    onSurface = VibeInk,
    surfaceVariant = Color(0xFFF2EDF5),
    onSurfaceVariant = VibeInkMuted,
    outline = Color(0xFFD9CFDC),
    error = Color(0xFFB3261E),
    onError = VibeWhite,
)

private val VibeTypography = Typography(
    displaySmall = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.ExtraBold,
        fontSize = 32.sp,
        lineHeight = 38.sp,
        letterSpacing = (-0.5).sp,
    ),
    headlineMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Bold,
        fontSize = 26.sp,
        lineHeight = 32.sp,
        letterSpacing = (-0.3).sp,
    ),
    titleLarge = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Bold,
        fontSize = 20.sp,
        lineHeight = 26.sp,
    ),
    titleMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 16.sp,
        lineHeight = 22.sp,
    ),
    bodyLarge = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Normal,
        fontSize = 16.sp,
        lineHeight = 22.sp,
    ),
    bodyMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Normal,
        fontSize = 14.sp,
        lineHeight = 20.sp,
    ),
    labelLarge = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Bold,
        fontSize = 14.sp,
    ),
    labelSmall = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Medium,
        fontSize = 12.sp,
    ),
)

private val VibeShapes = Shapes(
    extraSmall = RoundedCornerShape(10.dp),
    small = RoundedCornerShape(14.dp),
    medium = RoundedCornerShape(20.dp),
    large = RoundedCornerShape(24.dp),
    extraLarge = RoundedCornerShape(30.dp),
)

@Composable
fun VibeTheme(
    themeMode: ThemeMode = ThemeMode.DARK,
    content: @Composable () -> Unit,
) {
    val dark = when (themeMode) {
        ThemeMode.DARK -> true
        ThemeMode.LIGHT -> false
        ThemeMode.SYSTEM -> isSystemInDarkTheme()
    }
    MaterialTheme(
        colorScheme = if (dark) DarkScheme else LightScheme,
        typography = VibeTypography,
        shapes = VibeShapes,
        content = content,
    )
}

/** True when the app is drawing the midnight-navy canvas. */
@Composable
fun vibeIsDark(themeMode: ThemeMode): Boolean = when (themeMode) {
    ThemeMode.DARK -> true
    ThemeMode.LIGHT -> false
    ThemeMode.SYSTEM -> isSystemInDarkTheme()
}
