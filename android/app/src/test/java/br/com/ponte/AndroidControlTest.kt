package br.com.ponte

import org.junit.Assert.*
import org.junit.Test

class AndroidControlTest {
    @Test fun supportedCommandsAreBounded() {
        assertTrue(AndroidControl.parse("{\"kind\":\"tap\",\"x\":0.5,\"y\":0.4}").valid)
        assertTrue(AndroidControl("swipe", 0.1, 0.2, 0.8, 0.9).valid)
        assertFalse(AndroidControl("tap", Double.NaN).valid)
        assertFalse(AndroidControl("swipe", endX = 2.0).valid)
        assertFalse(AndroidControl("shell").valid)
        assertFalse(AndroidControl("text", text = "x".repeat(1001)).valid)
        assertFalse(AndroidControl("text", text = "\u0000").valid)
    }
    @Test fun protectedUiCannotBeTargeted() {
        for (name in listOf("br.com.ponte", "com.android.settings", "com.google.android.permissioncontroller", "com.android.systemui", "com.android.packageinstaller")) assertTrue(name, AndroidControl.protectedPackage(name))
        assertFalse(AndroidControl.protectedPackage("com.example.editor"))
    }
}
