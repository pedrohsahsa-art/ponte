package br.com.ponte

import org.junit.Assert.*
import org.junit.Test

class KeyboardEditsTest {
    @Test fun eachCharacterIsAnImmediateInsertion() {
        assertEquals(KeyboardEdit(0, "a"), KeyboardEdits.difference("", "a"))
        assertEquals(KeyboardEdit(0, "b"), KeyboardEdits.difference("a", "ab"))
    }
    @Test fun backspaceIsImmediateWithoutSendingWholeField() { assertEquals(KeyboardEdit(1, ""), KeyboardEdits.difference("ab", "a")) }
    @Test fun composingAccentReplacesThePreviousCharacter() { assertEquals(KeyboardEdit(1, "á"), KeyboardEdits.difference("a", "á")) }
    @Test fun autocorrectionRebuildsOnlyChangedSuffixAtCaret() { assertEquals(KeyboardEdit(1, "lo"), KeyboardEdits.difference("helo", "hello")) }
    @Test fun correctionBeforeSuffixKeepsItsOrder() { assertEquals(KeyboardEdit(7, "lo world"), KeyboardEdits.difference("helo world", "hello world")) }
    @Test fun emojiDeletionCountsCodePointsNotHalfSurrogates() { assertEquals(KeyboardEdit(1, ""), KeyboardEdits.difference("a😀", "a")) }
    @Test fun changingEmojiNeverKeepsHalfOfASurrogatePair() { assertEquals(KeyboardEdit(1, "😁"), KeyboardEdits.difference("😀", "😁")) }
    @Test fun chunkingPreservesAllTextAndEmojiBoundaries() {
        val value = "x".repeat(499) + "😀" + "y".repeat(1200)
        val commands = KeyboardEdits.commands(KeyboardEdit(600, value))
        assertEquals(600, commands.sumOf { it.delta })
        assertEquals(value, commands.joinToString("") { it.text ?: "" })
        assertTrue(commands.all { it.kind == "edit" && it.delta <= 500 && (it.text?.length ?: 0) <= 500 })
        assertTrue(commands.none { !it.text.isNullOrEmpty() && Character.isHighSurrogate(it.text!!.last()) })
    }
    @Test fun emptyChangesProduceNoNetworkInput() { assertTrue(KeyboardEdits.commands(KeyboardEdits.difference("a", "a")).isEmpty()) }
    @Test fun multilinePasteNormalizesWindowsNewlines() { assertEquals(KeyboardEdit(0, "a\nb\nc"), KeyboardEdits.difference("", "a\r\nb\rc")) }
}
