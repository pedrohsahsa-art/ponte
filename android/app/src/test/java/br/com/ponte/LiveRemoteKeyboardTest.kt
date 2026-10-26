package br.com.ponte

import android.content.Context
import android.text.InputType
import android.view.inputmethod.EditorInfo
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35])
class LiveRemoteKeyboardTest {
    private fun bridge(edits: MutableList<KeyboardEdit>): LiveRemoteKeyboard {
        val context = ApplicationProvider.getApplicationContext<Context>()
        return LiveRemoteKeyboard(context).apply { onEdit = { edits.add(it); true }; updateRequests(1, 0, 0, false) }
    }
    @Test fun softKeyboardCommitIsForwardedWithoutAnEnviarButton() {
        val edits = mutableListOf<KeyboardEdit>()
        val view = bridge(edits)
        val input = view.onCreateInputConnection(EditorInfo())!!
        input.commitText("a", 1)
        input.commitText("b", 1)
        assertEquals(listOf(KeyboardEdit(0, "a"), KeyboardEdit(0, "b")), edits)
    }
    @Test fun composingAccentAndDeletionAreSentAsLiveEdits() {
        val edits = mutableListOf<KeyboardEdit>()
        val view = bridge(edits)
        val input = view.onCreateInputConnection(EditorInfo())!!
        input.setComposingText("a", 1)
        input.setComposingText("á", 1)
        input.finishComposingText()
        input.deleteSurroundingText(1, 0)
        assertEquals(listOf(KeyboardEdit(0, "a"), KeyboardEdit(1, "á"), KeyboardEdit(1, "")), edits)
    }
    @Test fun backspaceWithEmptyLocalHistoryStillDeletesOnPc() {
        val edits = mutableListOf<KeyboardEdit>()
        val input = bridge(edits).onCreateInputConnection(EditorInfo())!!
        input.deleteSurroundingText(1, 0)
        assertEquals(listOf(KeyboardEdit(1, "")), edits)
    }
    @Test fun changingFieldClearsOnlyLocalHistoryNotRemoteText() {
        val edits = mutableListOf<KeyboardEdit>()
        val view = bridge(edits)
        view.onCreateInputConnection(EditorInfo())!!.commitText("word", 1)
        view.updateRequests(2, 0, 0, false)
        assertEquals(listOf(KeyboardEdit(0, "word")), edits)
        assertEquals("", view.text.toString())
    }
    @Test fun emojiDeletionFromImeProducesOneRemoteBackspace() {
        val edits = mutableListOf<KeyboardEdit>()
        val input = bridge(edits).onCreateInputConnection(EditorInfo())!!
        input.commitText("😀", 1)
        input.deleteSurroundingTextInCodePoints(1, 0)
        assertEquals(listOf(KeyboardEdit(0, "😀"), KeyboardEdit(1, "")), edits)
    }
    @Test fun passwordModeDisablesSavingAndRequestsNoLearning() {
        val edits = mutableListOf<KeyboardEdit>()
        val view = bridge(edits)
        view.updateRequests(2, 0, 0, true)
        val info = EditorInfo()
        view.onCreateInputConnection(info)
        assertFalse(view.isSaveEnabled)
        assertEquals(InputType.TYPE_TEXT_VARIATION_PASSWORD, view.inputType and InputType.TYPE_MASK_VARIATION)
        assertTrue(info.imeOptions and EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING != 0)
    }
}
