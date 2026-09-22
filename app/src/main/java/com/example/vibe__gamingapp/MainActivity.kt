package com.example.vibe__gamingapp

import android.content.Context
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.text.SpannableString
import android.text.InputType
import android.text.style.ForegroundColorSpan
import android.util.Base64
import android.view.Gravity
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Switch
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import java.security.SecureRandom
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.PBEKeySpec

class MainActivity : AppCompatActivity() {
    private val pink = Color.parseColor("#F73F8B")
    private val purple = Color.parseColor("#7B49C9")
    private val green = Color.parseColor("#37B88B")
    private val ink = Color.parseColor("#352C42")
    private val palePink = Color.parseColor("#FFF4F8")
    private val muted = Color.parseColor("#776D7B")
    private lateinit var auth: LocalAuth
    private var currentEmail: String? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        auth = LocalAuth(this)
        showLogin()
    }

    private fun showLogin(registering: Boolean = false) {
        val content = screen()
        content.addView(logo())
        title(content, if (registering) "Create your account" else "Welcome back")
        subtitle(content, if (registering) "Start making plans with your people." else "Stop arguing. Start vibing.")
        val name = input("Your name")
        val email = input("Email address", InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS)
        val password = input("Password", InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD)
        val confirm = input("Confirm password", InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD)
        if (registering) content.addView(name)
        content.addView(email); content.addView(password)
        if (registering) content.addView(confirm)
        content.addView(primaryButton(if (registering) "Create account" else "Log in") {
            val mail = email.text.toString().trim(); val secret = password.text.toString()
            if (registering) {
                val result = auth.register(name.text.toString().trim(), mail, secret, confirm.text.toString())
                if (result == null) { currentEmail = mail.lowercase(); showHome() } else toast(result)
            } else if (auth.verify(mail, secret)) { currentEmail = mail.lowercase(); showHome() }
            else toast("Email or password is incorrect.")
        })
        content.addView(TextView(this).apply {
            text = if (registering) "Already have an account? Log in" else "Don't have an account? Sign up"
            setTextColor(pink); gravity = Gravity.CENTER; setPadding(0, dp(18), 0, dp(8))
            setOnClickListener { showLogin(!registering) }
        })
        render(content)
    }

    private fun showHome() {
        val content = screen()
        val displayName = auth.name(currentEmail.orEmpty()).ifBlank { "Viber" }
        title(content, "Hey, $displayName! 👋"); subtitle(content, "What are we vibing today?")
        sectionHeader(content, "Your Groups", "View all →") { showGroups() }
        groupCard(content, "🎉", "Friday Night Crew", "8 members")
        groupCard(content, "💗", "Girls Trip", "6 members")
        groupCard(content, "🎮", "Game Squad", "5 members")
        content.addView(outlineButton("+  Create New Group") { toast("Group creation is coming next.") })
        sectionHeader(content, "Quick Start")
        quickStart(content, "💗", "Can't Decide?", "Start a yes/no decision game")
        quickStart(content, "🎲", "Surprise Me", "Let VIBE choose an activity")
        content.addView(bottomNav("Home")); render(content)
    }

    private fun showGroups() {
        val content = screen(); title(content, "Your Groups"); subtitle(content, "Your people, your plans.")
        groupCard(content, "🎉", "Friday Night Crew", "8 members · 4 activities")
        groupCard(content, "💗", "Girls Trip", "6 members · 7 activities")
        groupCard(content, "🎮", "Game Squad", "5 members · 3 activities")
        content.addView(primaryButton("+  Create New Group") { toast("Group creation is coming next.") })
        content.addView(bottomNav("Groups")); render(content)
    }

    private fun showSettings() {
        val content = screen(); title(content, "Settings"); subtitle(content, "Make VIBE feel like yours.")
        val name = input("Display name").apply { setText(auth.name(currentEmail.orEmpty())) }
        content.addView(label("Account")); content.addView(name)
        content.addView(primaryButton("Save name") {
            if (name.text.toString().trim().isBlank()) toast("Please enter a name.")
            else { auth.updateName(currentEmail.orEmpty(), name.text.toString().trim()); toast("Name updated.") }
        })
        content.addView(label("Change password"))
        val oldPassword = input("Current password", InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD)
        val newPassword = input("New password", InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD)
        val confirmPassword = input("Confirm new password", InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD)
        content.addView(oldPassword); content.addView(newPassword); content.addView(confirmPassword)
        content.addView(outlineButton("Update password") {
            val result = auth.changePassword(currentEmail.orEmpty(), oldPassword.text.toString(), newPassword.text.toString(), confirmPassword.text.toString())
            if (result == null) { oldPassword.text.clear(); newPassword.text.clear(); confirmPassword.text.clear(); toast("Password updated securely.") } else toast(result)
        })
        content.addView(label("Preferences"))
        content.addView(preferenceRow("🔔", "Notifications", "Updates from your groups", true))
        content.addView(preferenceRow("🌍", "Language", "English"))
        content.addView(outlineButton("Sign out") { currentEmail = null; showLogin() })
        content.addView(bottomNav("Settings")); render(content)
    }

    private fun showActivities() {
        val content = screen(); title(content, "Activities"); subtitle(content, "Ideas for your next VIBE.")
        quickStart(content, "🍕", "Go for pizza", "Added by Sarah")
        quickStart(content, "🎳", "Bowling night", "Added by Lizzy")
        quickStart(content, "🍿", "Movie marathon", "Added by John")
        content.addView(bottomNav("Activities")); render(content)
    }

    private fun screen() = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setBackgroundColor(palePink); setPadding(dp(20), dp(30), dp(20), dp(18)) }
    private fun render(content: LinearLayout) = setContentView(ScrollView(this).apply {
        setBackgroundColor(palePink)
        addView(content, android.widget.FrameLayout.LayoutParams(android.widget.FrameLayout.LayoutParams.MATCH_PARENT, android.widget.FrameLayout.LayoutParams.WRAP_CONTENT))
    })
    private fun logo() = TextView(this).apply {
        text = SpannableString("VIBE").apply {
            setSpan(ForegroundColorSpan(pink), 0, 1, 0)
            setSpan(ForegroundColorSpan(purple), 1, 2, 0)
            setSpan(ForegroundColorSpan(green), 2, 3, 0)
            setSpan(ForegroundColorSpan(pink), 3, 4, 0)
        }
        textSize = 34f; typeface = Typeface.DEFAULT_BOLD; gravity = Gravity.CENTER; setPadding(0, dp(14), 0, dp(20))
    }
    private fun title(parent: LinearLayout, text: String) = parent.addView(TextView(this).apply { this.text = text; textSize = 26f; typeface = Typeface.DEFAULT_BOLD; setTextColor(ink); setPadding(0, dp(4), 0, dp(4)) })
    private fun subtitle(parent: LinearLayout, text: String) = parent.addView(TextView(this).apply { this.text = text; textSize = 15f; setTextColor(muted); setPadding(0, 0, 0, dp(18)) })
    private fun label(text: String) = TextView(this).apply { this.text = text.uppercase(); textSize = 12f; typeface = Typeface.DEFAULT_BOLD; setTextColor(purple); setPadding(0, dp(18), 0, dp(6)) }
    private fun input(hint: String, inputType: Int = InputType.TYPE_CLASS_TEXT) = EditText(this).apply {
        this.hint = hint; this.inputType = inputType; textSize = 16f; setTextColor(ink); setHintTextColor(muted); setPadding(dp(16), 0, dp(16), 0)
        background = rounded(Color.WHITE, dp(16), Color.parseColor("#EBCFE0"), 1)
        layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(54)).apply { bottomMargin = dp(10) }
    }
    private fun primaryButton(text: String, onClick: () -> Unit) = Button(this).apply {
        this.text = text; setTextColor(Color.WHITE); textSize = 15f; typeface = Typeface.DEFAULT_BOLD; background = rounded(pink, dp(18)); setOnClickListener { onClick() }
        layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(52)).apply { topMargin = dp(4); bottomMargin = dp(4) }
    }
    private fun outlineButton(text: String, onClick: () -> Unit) = Button(this).apply {
        this.text = text; setTextColor(purple); textSize = 15f; typeface = Typeface.DEFAULT_BOLD; background = rounded(Color.WHITE, dp(18), purple, 1); setOnClickListener { onClick() }
        layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(50)).apply { topMargin = dp(3); bottomMargin = dp(4) }
    }
    private fun sectionHeader(parent: LinearLayout, heading: String, action: String? = null, onAction: (() -> Unit)? = null) {
        val row = LinearLayout(this).apply { gravity = Gravity.CENTER_VERTICAL; setPadding(0, dp(18), 0, dp(7)) }
        row.addView(TextView(this).apply { text = heading; textSize = 19f; typeface = Typeface.DEFAULT_BOLD; setTextColor(ink) }, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        if (action != null) row.addView(TextView(this).apply { text = action; textSize = 14f; setTextColor(pink); setOnClickListener { onAction?.invoke() } })
        parent.addView(row)
    }
    private fun groupCard(parent: LinearLayout, emoji: String, name: String, detail: String) = card(parent, emoji, name, detail)
    private fun quickStart(parent: LinearLayout, emoji: String, name: String, detail: String) = card(parent, emoji, name, detail)
    private fun card(parent: LinearLayout, emoji: String, name: String, detail: String) {
        val row = LinearLayout(this).apply { gravity = Gravity.CENTER_VERTICAL; setPadding(dp(14), dp(12), dp(14), dp(12)); background = rounded(Color.WHITE, dp(18)); layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(72)).apply { bottomMargin = dp(8) } }
        row.addView(TextView(this).apply { text = emoji; textSize = 28f; gravity = Gravity.CENTER }, LinearLayout.LayoutParams(dp(46), LinearLayout.LayoutParams.MATCH_PARENT))
        val copy = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        copy.addView(TextView(this).apply { text = name; textSize = 16f; typeface = Typeface.DEFAULT_BOLD; setTextColor(ink) })
        copy.addView(TextView(this).apply { text = detail; textSize = 13f; setTextColor(muted) })
        row.addView(copy, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)); row.addView(TextView(this).apply { text = "›"; textSize = 28f; setTextColor(purple) }); parent.addView(row)
    }
    private fun preferenceRow(icon: String, heading: String, detail: String, switch: Boolean = false) = LinearLayout(this).apply {
        gravity = Gravity.CENTER_VERTICAL; setPadding(dp(14), dp(10), dp(14), dp(10)); background = rounded(Color.WHITE, dp(18)); layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(70)).apply { bottomMargin = dp(8) }
        addView(TextView(this@MainActivity).apply { text = icon; textSize = 23f }, LinearLayout.LayoutParams(dp(42), LinearLayout.LayoutParams.WRAP_CONTENT))
        val copy = LinearLayout(this@MainActivity).apply { orientation = LinearLayout.VERTICAL }
        copy.addView(TextView(this@MainActivity).apply { text = heading; textSize = 16f; typeface = Typeface.DEFAULT_BOLD; setTextColor(ink) }); copy.addView(TextView(this@MainActivity).apply { text = detail; textSize = 13f; setTextColor(muted) })
        addView(copy, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)); if (switch) addView(Switch(this@MainActivity).apply { isChecked = true }) else addView(TextView(this@MainActivity).apply { text = "›"; textSize = 28f; setTextColor(purple) })
    }
    private fun bottomNav(active: String) = LinearLayout(this).apply {
        gravity = Gravity.CENTER; setPadding(0, dp(18), 0, 0)
        listOf("Home", "Activities", "+", "Groups", "Settings").forEach { item -> addView(TextView(this@MainActivity).apply {
            text = item; textSize = if (item == "+") 24f else 11f; gravity = Gravity.CENTER; setTextColor(if (item == active || item == "+") pink else muted); typeface = if (item == active || item == "+") Typeface.DEFAULT_BOLD else Typeface.DEFAULT
            setOnClickListener { when (item) { "Home" -> showHome(); "Activities" -> showActivities(); "Groups" -> showGroups(); "Settings" -> showSettings(); "+" -> toast("Create an activity or group next.") } }
        }, LinearLayout.LayoutParams(0, dp(48), 1f)) }
    }
    private fun rounded(fill: Int, radius: Int, stroke: Int? = null, width: Int = 0) = GradientDrawable().apply { setColor(fill); cornerRadius = radius.toFloat(); if (stroke != null) setStroke(dp(width), stroke) }
    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()
    private fun toast(message: String) = Toast.makeText(this, message, Toast.LENGTH_SHORT).show()
}

/** Passwords are never persisted. A salted PBKDF2 hash is kept only for this local demo. */
private class LocalAuth(context: Context) {
    private val prefs = context.getSharedPreferences("vibe_accounts", Context.MODE_PRIVATE)
    private val iterations = 120_000
    fun register(name: String, email: String, password: String, confirmation: String): String? {
        val key = email.lowercase().trim()
        if (name.isBlank()) return "Please enter your name."
        if (!android.util.Patterns.EMAIL_ADDRESS.matcher(key).matches()) return "Enter a valid email address."
        if (prefs.contains("hash_$key")) return "An account already exists for this email."
        passwordProblem(password)?.let { return it }; if (password != confirmation) return "Passwords do not match."
        prefs.edit().putString("name_$key", name).putString("hash_$key", hash(password)).apply(); return null
    }
    fun verify(email: String, password: String): Boolean { val stored = prefs.getString("hash_${email.lowercase().trim()}", null) ?: return false; return verifyHash(password, stored) }
    fun name(email: String): String = prefs.getString("name_${email.lowercase().trim()}", "") ?: ""
    fun updateName(email: String, name: String) = prefs.edit().putString("name_${email.lowercase().trim()}", name).apply()
    fun changePassword(email: String, old: String, fresh: String, confirmation: String): String? {
        if (!verify(email, old)) return "Your current password is incorrect."
        passwordProblem(fresh)?.let { return it }; if (fresh != confirmation) return "New passwords do not match."
        prefs.edit().putString("hash_${email.lowercase().trim()}", hash(fresh)).apply(); return null
    }
    private fun passwordProblem(password: String): String? = when { password.length < 8 -> "Use at least 8 characters."; password.none { it.isDigit() } -> "Include at least one number."; else -> null }
    private fun hash(password: String): String { val salt = ByteArray(16).also { SecureRandom().nextBytes(it) }; val derived = derive(password, salt); return "$iterations:${Base64.encodeToString(salt, Base64.NO_WRAP)}:${Base64.encodeToString(derived, Base64.NO_WRAP)}" }
    private fun verifyHash(password: String, stored: String): Boolean = try { val parts = stored.split(":"); if (parts.size != 3) false else { val salt = Base64.decode(parts[1], Base64.NO_WRAP); val expected = Base64.decode(parts[2], Base64.NO_WRAP); java.security.MessageDigest.isEqual(expected, derive(password, salt, parts[0].toInt())) } } catch (_: Exception) { false }
    private fun derive(password: String, salt: ByteArray, rounds: Int = iterations): ByteArray { val spec = PBEKeySpec(password.toCharArray(), salt, rounds, 256); return try { SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(spec).encoded } finally { spec.clearPassword() } }
}
