package com.vibe.app.domain

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.random.Random

/**
 * Form validation and invite codes.
 *
 * The rules match the ones the ASP.NET Core API enforces, so a value that passes
 * on the device is not rejected by the server - and the errors are [FieldError]
 * values, which is what lets the UI show them in English, isiZulu or Sesotho.
 */
class ValidationTest {

    @Test
    fun `a display name is required and cannot run away`() {
        assertEquals(FieldError.NAME_REQUIRED, Validators.displayName("   "))
        assertEquals(FieldError.NAME_TOO_LONG, Validators.displayName("L".repeat(Validators.MAX_NAME_LENGTH + 1)))
        assertNull(Validators.displayName("Lerato"))
    }

    @Test
    fun `email addresses are checked the same way on both sides`() {
        assertNull(Validators.email("lerato@vibe.app"))
        assertNull(Validators.email("  lerato.mokoena+poe@student.ac.za "))
        assertEquals(FieldError.EMAIL_INVALID, Validators.email("lerato@vibe"))
        assertEquals(FieldError.EMAIL_INVALID, Validators.email("not-an-email"))
        assertEquals(FieldError.EMAIL_INVALID, Validators.email(""))
    }

    @Test
    fun `passwords follow the OWASP aligned policy`() {
        assertEquals(FieldError.PASSWORD_SHORT, Validators.password("vibe1"))
        assertEquals(FieldError.PASSWORD_DIGIT, Validators.password("vibevibevibe"))
        assertNull(Validators.password("vibe2026go"))
    }

    @Test
    fun `the confirmation has to match`() {
        assertEquals(FieldError.PASSWORD_MISMATCH, Validators.passwordConfirmation("vibe2026", "vibe2027"))
        assertNull(Validators.passwordConfirmation("vibe2026", "vibe2026"))
    }

    @Test
    fun `groups and activities need a name, captions have a limit`() {
        assertEquals(FieldError.GROUP_NAME_REQUIRED, Validators.groupName("  "))
        assertEquals(FieldError.ACTIVITY_TITLE_REQUIRED, Validators.activityTitle(""))
        assertNull(Validators.groupName("Friday Night Crew"))
        assertEquals(
            FieldError.CAPTION_TOO_LONG,
            Validators.caption("x".repeat(Validators.MAX_CAPTION_LENGTH + 1)),
        )
        assertNull(Validators.caption("Best pizza in Mzansi."))
    }

    @Test
    fun `a memory rating is between one and five stars`() {
        assertNull(Validators.rating(5))
        assertEquals(FieldError.RATING_OUT_OF_RANGE, Validators.rating(6))
        assertEquals(FieldError.RATING_OUT_OF_RANGE, Validators.rating(-1))
    }

    @Test
    fun `a round needs at least two ideas and an owner to start it`() {
        assertEquals(FieldError.NOT_ENOUGH_ACTIVITIES, Validators.roundActivities(1))
        assertNull(Validators.roundActivities(2))
        assertEquals(FieldError.OWNER_ONLY, Validators.canStartRound(GroupRole.MEMBER))
        assertNull(Validators.canStartRound(GroupRole.OWNER))
    }

    @Test
    fun `invite codes are six characters from a read-aloud friendly alphabet`() {
        val random = Random(42)
        repeat(200) {
            val code = InviteCode.generate(random)
            assertEquals(InviteCode.LENGTH, code.length)
            assertTrue(code.all { it in InviteCode.ALPHABET })
            assertTrue(InviteCode.isValid(code))
        }
        // No I, O, 0 or 1: the codes are read out loud or typed from a screenshot.
        assertTrue(InviteCode.ALPHABET.none { it in "IO01" })
    }

    @Test
    fun `codes are accepted in any case and with stray spaces`() {
        assertTrue(InviteCode.isValid(" fryday "))
        assertTrue(InviteCode.isValid("FryDay"))
        assertEquals("FRYDAY", InviteCode.normalise(" fryday "))
        assertFalse(InviteCode.isValid("FRYDA"))
        assertFalse(InviteCode.isValid("FRYDAY1"))
        assertEquals(FieldError.INVITE_CODE_INVALID, Validators.inviteCode("nope"))
        assertNull(Validators.inviteCode("FRYDAY"))
    }

    @Test
    fun `an offline action gets a stable client id so a retry is never applied twice`() {
        val first = Ids.syncClientId(SyncAction.ADD_ACTIVITY, "act-1", 1_700_000_000_000L)
        val second = Ids.syncClientId(SyncAction.ADD_ACTIVITY, "act-1", 1_700_000_000_000L)
        assertEquals(first, second)
        assertTrue(first.startsWith("activity.add:act-1:"))
        assertFalse(first == Ids.syncClientId(SyncAction.ADD_ACTIVITY, "act-1", 1_700_000_000_001L))
    }

    @Test
    fun `every queued action round-trips through its wire name`() {
        SyncAction.entries.forEach { action ->
            assertEquals(action, SyncAction.fromWire(action.wire))
        }
        assertNull(SyncAction.fromWire("not.an.action"))
    }

    @Test
    fun `a language tag maps back to the language the member picked`() {
        assertEquals(AppLanguage.ISIZULU, AppLanguage.fromTag("zu"))
        assertEquals(AppLanguage.SESOTHO, AppLanguage.fromTag("st"))
        assertEquals(AppLanguage.ENGLISH, AppLanguage.fromTag("EN"))
        assertEquals(AppLanguage.ENGLISH, AppLanguage.fromTag(null))
        assertEquals(AppLanguage.ENGLISH, AppLanguage.fromTag("fr"))
    }
}
