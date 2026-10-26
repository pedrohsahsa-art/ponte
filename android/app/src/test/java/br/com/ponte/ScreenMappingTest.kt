package br.com.ponte

import org.junit.Assert.*
import org.junit.Test

class ScreenMappingTest {
    @Test fun zoomedAndPannedClicksStillTargetCorrectDesktopPixels() {
        val point = ScreenMapping.point(1000, 500, 1000, 500, 700f, 250f, ViewportTransform(2f, 200f, 0f))!!
        assertEquals(0.5, point.x, 0.00001)
        assertEquals(0.5, point.y, 0.00001)
    }
    @Test fun fillUsesAvailableScreenWithoutStretchingAspectRatio() {
        val fill = ScreenMapping.fill(400, 800, 1280, 720)
        assertEquals(800f / 225f, fill.zoom, 0.00001f)
        assertEquals(0.5, ScreenMapping.point(400, 800, 1280, 720, 200f, 400f, fill)!!.x, 0.00001)
        assertNotNull(ScreenMapping.point(400, 800, 1280, 720, 200f, 0f, fill))
    }
    @Test fun panningNeverLosesTheImageOutsideViewport() {
        val bounded = ScreenMapping.clamp(1000, 500, 1000, 500, ViewportTransform(2f, 9000f, -9000f))
        assertEquals(500f, bounded.panX, 0.00001f)
        assertEquals(-250f, bounded.panY, 0.00001f)
        assertEquals(ViewportTransform(), ScreenMapping.clamp(1000, 500, 1000, 500, ViewportTransform(1f, 10f, 20f)))
    }
    @Test fun pinchPreservesTheDesktopPointUnderTheFingers() {
        val old = ViewportTransform(2f, 0f, 0f)
        val before = ScreenMapping.point(1000, 500, 1000, 500, 650f, 300f, old)!!
        val changed = ScreenMapping.zoomAround(1000, 500, 1000, 500, old, 1.3f, 650f, 300f)
        val after = ScreenMapping.point(1000, 500, 1000, 500, 650f, 300f, changed)!!
        assertEquals(before.x, after.x, 0.00001)
        assertEquals(before.y, after.y, 0.00001)
    }
    @Test fun zoomAndPanSanitizeInvalidValues() {
        assertEquals(ViewportTransform(), ScreenMapping.clamp(1000, 500, 1000, 500, ViewportTransform(Float.NaN, Float.NaN, Float.NaN)))
        assertNull(ScreenMapping.point(1000, 500, 1000, 500, 0f, 0f, ViewportTransform(Float.NaN)))
    }
    @Test fun mapsCenterWithPortraitLetterbox() {
        val point = ScreenMapping.point(400, 800, 1280, 720, 200f, 400f)!!
        assertEquals(0.5, point.x, 0.00001)
        assertEquals(0.5, point.y, 0.00001)
    }
    @Test fun rejectsTouchesInTopAndBottomBars() {
        assertNull(ScreenMapping.point(400, 800, 1280, 720, 200f, 100f))
        assertNull(ScreenMapping.point(400, 800, 1280, 720, 200f, 700f))
    }
    @Test fun mapsActualImageEdges() {
        val topLeft = ScreenMapping.point(400, 800, 1280, 720, 0f, 287.5f)!!
        val bottomRight = ScreenMapping.point(400, 800, 1280, 720, 400f, 512.5f)!!
        assertEquals(0.0, topLeft.x, 0.00001)
        assertEquals(0.0, topLeft.y, 0.00001)
        assertEquals(1.0, bottomRight.x, 0.00001)
        assertEquals(1.0, bottomRight.y, 0.00001)
    }
    @Test fun mapsWideLandscapeWithSideBars() {
        assertNull(ScreenMapping.point(1000, 300, 1280, 720, 100f, 150f))
        val point = ScreenMapping.point(1000, 300, 1280, 720, 500f, 150f)!!
        assertEquals(0.5, point.x, 0.00001)
        assertEquals(0.5, point.y, 0.00001)
    }
    @Test fun rejectsUnmeasuredViewsAndInvalidCoordinates() {
        assertNull(ScreenMapping.point(0, 300, 1280, 720, 0f, 0f))
        assertNull(ScreenMapping.point(400, 300, 0, 720, 0f, 0f))
        assertNull(ScreenMapping.point(400, 300, 1280, 720, Float.NaN, 0f))
        assertNull(ScreenMapping.point(400, 300, 1280, 720, Float.POSITIVE_INFINITY, 0f))
    }
}
