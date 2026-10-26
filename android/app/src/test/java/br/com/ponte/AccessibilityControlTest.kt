package br.com.ponte

import android.accessibilityservice.AccessibilityService
import android.view.accessibility.AccessibilityNodeInfo
import org.junit.After
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import org.robolectric.RobolectricTestRunner
import org.robolectric.Shadows.shadowOf
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35])
class AccessibilityControlTest {
    @After fun reset() { AndroidShareService.authorized = false }
    @Test fun interruptionOfFeedbackDoesNotRevokeCaptureConsent() {
        val controller = Robolectric.buildService(PonteAccessibilityService::class.java).create()
        AndroidShareService.authorized = true
        controller.get().onInterrupt()
        assertTrue(AndroidShareService.authorized)
        controller.destroy()
    }
    @Test fun homeCanLeavePonteButClicksInsidePonteAreStillBlocked() {
        val controller = Robolectric.buildService(PonteAccessibilityService::class.java).create()
        val service = controller.get()
        @Suppress("DEPRECATION") val root = AccessibilityNodeInfo.obtain().apply { packageName = "br.com.ponte" }
        shadowOf(service).setRootInActiveWindow(root)
        AndroidShareService.authorized = true
        var result = ""
        service.execute(AndroidControl("tap", .5, .5)) { result = it }
        assertEquals("protected_window", result)
        assertTrue(shadowOf(service).gesturesDispatched.isEmpty())
        service.execute(AndroidControl("home")) { result = it }
        assertEquals("navigation_sent", result)
        assertTrue(shadowOf(service).globalActionsPerformed.contains(AccessibilityService.GLOBAL_ACTION_HOME))
        controller.destroy()
    }
    @Test fun captureConsentIsStillRequiredForGlobalNavigation() {
        val controller = Robolectric.buildService(PonteAccessibilityService::class.java).create()
        var result = ""
        controller.get().execute(AndroidControl("back")) { result = it }
        assertEquals("sharing_inactive", result)
        assertTrue(shadowOf(controller.get()).globalActionsPerformed.isEmpty())
        controller.destroy()
    }
    @Test fun missingWindowDoesNotSilentlyDropInput() {
        val controller = Robolectric.buildService(PonteAccessibilityService::class.java).create()
        shadowOf(controller.get()).setWindows(emptyList())
        AndroidShareService.authorized = true
        var result = ""
        controller.get().execute(AndroidControl("tap", .5, .5)) { result = it }
        assertEquals("window_unavailable", result)
        controller.destroy()
    }
    @Test fun absentGestureCapabilityIsReportedBeforeDispatch() {
        val controller = Robolectric.buildService(PonteAccessibilityService::class.java).create()
        @Suppress("DEPRECATION") val root = AccessibilityNodeInfo.obtain().apply { packageName = "com.example.editor" }
        shadowOf(controller.get()).setRootInActiveWindow(root)
        AndroidShareService.authorized = true
        var result = ""
        controller.get().execute(AndroidControl("tap", .5, .5)) { result = it }
        assertEquals("gestures_unavailable", result)
        assertTrue(shadowOf(controller.get()).gesturesDispatched.isEmpty())
        controller.destroy()
    }
}
