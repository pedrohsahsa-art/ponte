package br.com.ponte

import android.content.Context
import android.graphics.Color
import android.text.Editable
import android.text.InputType
import android.text.TextWatcher
import android.view.KeyEvent
import android.view.View
import android.view.inputmethod.BaseInputConnection
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputConnection
import android.view.inputmethod.InputConnectionWrapper
import android.view.inputmethod.InputMethodManager
import android.widget.EditText

// Tiny, transparent native editor used solely as an IME bridge. No saved state,
// autofill, accessibility text export or remote field contents. Nothing is logged.
class LiveRemoteKeyboard(context: Context) : EditText(context) {
    var onEdit: (KeyboardEdit) -> Boolean = { false }
    var onRemoteKey: (String) -> Unit = { }
    private var muted = true
    private var depth = 0
    private var snapshot = ""
    private var lastOpen = 0
    private var lastHide = 0
    private var lastReset = 0
    private val ime get() = context.getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager
    init {
        isFocusableInTouchMode = true
        isSaveEnabled = false
        importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO_EXCLUDE_DESCENDANTS
        importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
        setTextColor(Color.TRANSPARENT)
        setBackgroundColor(Color.TRANSPARENT)
        alpha = 0.01f
        imeOptions = EditorInfo.IME_ACTION_NONE or EditorInfo.IME_FLAG_NO_EXTRACT_UI or EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING
        addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) { }
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) { }
            override fun afterTextChanged(s: Editable?) { if (!muted && depth == 0) synchronize() }
        })
        muted = false
    }
    fun updateRequests(open: Int, hide: Int, reset: Int, password: Boolean) {
        if (hide != lastHide) { lastHide = hide; dismiss() }
        if (reset != lastReset) { lastReset = reset; resetHistory(); ime.restartInput(this) }
        if (open != lastOpen) {
            lastOpen = open
            if (open <= hide) return // a newer hide must win over a delayed open
            muted = true
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS or
                if (password) InputType.TYPE_TEXT_VARIATION_PASSWORD else InputType.TYPE_TEXT_FLAG_MULTI_LINE
            resetHistory()
            muted = false
            requestFocus()
            ime.restartInput(this)
            post { if (hasFocus()) ime.showSoftInput(this, InputMethodManager.SHOW_IMPLICIT) }
        }
    }
    fun resetHistory() {
        val previous = muted
        muted = true
        setText("")
        snapshot = ""
        muted = previous
    }
    fun dismiss() { ime.hideSoftInputFromWindow(windowToken, 0); clearFocus(); resetHistory() }
    private fun synchronize(extraDeletes: Int = 0) {
        if (muted) return
        val current = text?.toString() ?: ""
        val change = KeyboardEdits.difference(snapshot, current)
        snapshot = current
        if ((change.deletes > 0 || change.text.isNotEmpty() || extraDeletes > 0) && !onEdit(change.copy(deletes = change.deletes + extraDeletes))) {
            dismiss()
            return
        }
        // Bound local history, without deleting anything on the PC.
        if (current.length > 2048 && BaseInputConnection.getComposingSpanStart(text) < 0) {
            resetHistory()
            post { ime.restartInput(this) }
        }
    }
    private inline fun change(extraDeletes: Int = 0, action: () -> Boolean): Boolean {
        depth++
        return try { action() } finally { depth--; if (depth == 0) synchronize(extraDeletes) }
    }
    override fun onCreateInputConnection(outAttrs: EditorInfo): InputConnection? {
        val base = super.onCreateInputConnection(outAttrs) ?: return null
        outAttrs.imeOptions = imeOptions
        return object : InputConnectionWrapper(base, true) {
            override fun commitText(text: CharSequence?, newCursorPosition: Int) = change { super.commitText(text, newCursorPosition) }
            override fun setComposingText(text: CharSequence?, newCursorPosition: Int) = change { super.setComposingText(text, newCursorPosition) }
            override fun finishComposingText() = change { super.finishComposingText() }
            override fun deleteSurroundingText(beforeLength: Int, afterLength: Int): Boolean {
                val missing = (beforeLength - selectionStart.coerceAtLeast(0)).coerceAtLeast(0)
                return change(missing) { super.deleteSurroundingText(beforeLength, afterLength) }
            }
            override fun deleteSurroundingTextInCodePoints(beforeLength: Int, afterLength: Int): Boolean {
                val value = this@LiveRemoteKeyboard.text?.toString() ?: ""
                val available = value.codePointCount(0, selectionStart.coerceIn(0, value.length))
                return change((beforeLength - available).coerceAtLeast(0)) { super.deleteSurroundingTextInCodePoints(beforeLength, afterLength) }
            }
            override fun setSelection(start: Int, end: Int): Boolean {
                val length = this@LiveRemoteKeyboard.text?.length ?: 0
                return super.setSelection(length, length)
            }
            override fun performEditorAction(editorAction: Int): Boolean {
                onRemoteKey("enter"); resetHistory(); return true
            }
        }
    }
    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean {
        val key = when (keyCode) {
            KeyEvent.KEYCODE_ENTER -> "enter"
            KeyEvent.KEYCODE_TAB -> "tab"
            KeyEvent.KEYCODE_DPAD_LEFT -> "left"
            KeyEvent.KEYCODE_DPAD_RIGHT -> "right"
            KeyEvent.KEYCODE_DPAD_UP -> "up"
            KeyEvent.KEYCODE_DPAD_DOWN -> "down"
            KeyEvent.KEYCODE_FORWARD_DEL -> "delete"
            else -> null
        }
        if (key != null) { onRemoteKey(key); resetHistory(); return true }
        if (keyCode == KeyEvent.KEYCODE_DEL && text.isNullOrEmpty()) return onEdit(KeyboardEdit(1, ""))
        return super.onKeyDown(keyCode, event)
    }
}
