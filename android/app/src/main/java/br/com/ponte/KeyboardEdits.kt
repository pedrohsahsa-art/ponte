package br.com.ponte

data class KeyboardEdit(val deletes: Int, val text: String)
data class KeyboardTarget(val editable: Boolean, val password: Boolean = false, val focusId: String = "", val available: Boolean = true)

object KeyboardEdits {
    // The phone holds only its own recent IME composition, never the PC field's value.
    // Rebuild the changed suffix at the remote caret, preserving correction order.
    fun difference(before: String, after: String): KeyboardEdit {
        val old = before.replace("\r\n", "\n").replace('\r', '\n')
        val new = after.replace("\r\n", "\n").replace('\r', '\n')
        var common = 0
        while (common < old.length && common < new.length && old[common] == new[common]) common++
        if (common > 0 && common < old.length && Character.isHighSurrogate(old[common - 1]) && Character.isLowSurrogate(old[common])) common--
        return KeyboardEdit(old.codePointCount(common, old.length), new.substring(common))
    }
    fun commands(edit: KeyboardEdit): List<RemoteCommand> {
        require(edit.deletes >= 0)
        val result = mutableListOf<RemoteCommand>()
        var remaining = edit.deletes
        while (remaining > 500) { result += RemoteCommand("edit", delta = 500, text = ""); remaining -= 500 }
        var offset = 0
        do {
            var end = minOf(edit.text.length, offset + 500)
            if (end < edit.text.length && end > offset && Character.isHighSurrogate(edit.text[end - 1]) && Character.isLowSurrogate(edit.text[end])) end--
            val chunk = edit.text.substring(offset, end)
            if (remaining > 0 || chunk.isNotEmpty()) result += RemoteCommand("edit", delta = remaining, text = chunk)
            remaining = 0
            offset = end
        } while (offset < edit.text.length)
        return result
    }
}
