package br.com.ponte

import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.AccessibilityServiceInfo
import android.accessibilityservice.GestureDescription
import android.app.KeyguardManager
import android.graphics.Path
import android.os.Bundle
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo
import android.content.Intent
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

class PonteAccessibilityService : AccessibilityService() {
    companion object {
        @Volatile var current: PonteAccessibilityService? = null; private set
        var connected by mutableStateOf(false); private set
    }
    override fun onServiceConnected() { current = this; connected = true }
    private fun disconnected() { if (current === this) { current = null; connected = false } }
    override fun onUnbind(intent: Intent?): Boolean { disconnected(); return super.onUnbind(intent) }
    override fun onDestroy() { disconnected(); super.onDestroy() }
    override fun onAccessibilityEvent(event: AccessibilityEvent?) { } // never inspect or log event text
    // Interrupts accessibility feedback, not the separate capture consent.
    // Capture stop, lock, disconnect and explicit stop still revoke that consent.
    override fun onInterrupt() { }
    private fun activeRoot(): AccessibilityNodeInfo? = rootInActiveWindow
        ?: windows.firstOrNull { it.isActive || it.isFocused }?.root
    fun availability(): String {
        if (!AndroidShareService.authorized) return "sharing_inactive"
        if (getSystemService(KeyguardManager::class.java).isDeviceLocked) return "locked"
        val root = activeRoot() ?: return "window_unavailable"
        if (AndroidControl.protectedPackage(root.packageName?.toString() ?: "br.com.ponte")) return "protected_window"
        return if ((serviceInfo?.capabilities ?: 0) and AccessibilityServiceInfo.CAPABILITY_CAN_PERFORM_GESTURES == 0) "gestures_unavailable" else "ready"
    }
    fun execute(command: AndroidControl, result: (String) -> Unit = {}) {
        if (!AndroidShareService.authorized) { result("sharing_inactive"); return }
        if (!command.valid) { result("invalid_command"); return }
        if (getSystemService(KeyguardManager::class.java).isDeviceLocked) { result("locked"); return }
        // Safe exit navigation does not require a window tree. It cannot approve
        // a permission. Tap/swipe/text still reject protected apps and dialogs.
        if (command.kind == "back" || command.kind == "home") {
            result(if (performGlobalAction(if (command.kind == "back") GLOBAL_ACTION_BACK else GLOBAL_ACTION_HOME)) "navigation_sent" else "action_rejected"); return
        }
        val root = activeRoot() ?: run { result("window_unavailable"); return }
        if (AndroidControl.protectedPackage(root.packageName?.toString() ?: "br.com.ponte")) { result("protected_window"); return }
        when (command.kind) {
            "recents" -> result(if (performGlobalAction(GLOBAL_ACTION_RECENTS)) "navigation_sent" else "action_rejected")
            "tap", "swipe" -> {
                if ((serviceInfo?.capabilities ?: 0) and AccessibilityServiceInfo.CAPABILITY_CAN_PERFORM_GESTURES == 0) { result("gestures_unavailable"); return }
                val size = AndroidShareService.physicalSize ?: run { result("sharing_inactive"); return }
                val path = Path().apply { moveTo((command.x * (size.first - 1)).toFloat(), (command.y * (size.second - 1)).toFloat()); if (command.kind == "swipe") lineTo((command.endX * (size.first - 1)).toFloat(), (command.endY * (size.second - 1)).toFloat()) }
                val accepted = dispatchGesture(GestureDescription.Builder().addStroke(GestureDescription.StrokeDescription(path, 0, if (command.kind == "tap") 70 else 350)).build(), object : GestureResultCallback() {
                    override fun onCompleted(gestureDescription: GestureDescription?) { result("gesture_completed") }
                    override fun onCancelled(gestureDescription: GestureDescription?) { result("action_rejected") }
                }, null)
                result(if (accepted) "gesture_sent" else "action_rejected")
            }
            "text" -> {
                val focused = root.findFocus(AccessibilityNodeInfo.FOCUS_INPUT) ?: run { result("no_editable_field"); return }
                if (focused.isPassword) { result("password_field"); return }
                if (!focused.isEditable || !focused.isEnabled) { result("no_editable_field"); return }
                if (AndroidControl.protectedPackage(focused.packageName?.toString() ?: "br.com.ponte")) { result("protected_window"); return }
                val accepted = focused.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, Bundle().apply { putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, command.text) })
                result(if (accepted) "text_applied" else "action_rejected")
            }
        }
    }
}
