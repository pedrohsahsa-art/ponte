package br.com.ponte

data class RemoteSession(val sessionId: String, val intervalMs: Long)
data class RemoteCommand(val kind: String, val x: Double = 0.0, val y: Double = 0.0, val delta: Int = 0, val text: String? = null, val key: String? = null)
data class ScreenPoint(val x: Double, val y: Double)
data class ViewportTransform(val zoom: Float = 1f, val panX: Float = 0f, val panY: Float = 0f)

// Matches ContentScale.Fit, including letterboxing and portrait/landscape layouts.
object ScreenMapping {
    fun point(viewWidth: Int, viewHeight: Int, frameWidth: Int, frameHeight: Int, x: Float, y: Float, transform: ViewportTransform = ViewportTransform()): ScreenPoint? {
        if (viewWidth <= 0 || viewHeight <= 0 || frameWidth <= 0 || frameHeight <= 0 || !x.isFinite() || !y.isFinite()) return null
        if (!transform.zoom.isFinite() || transform.zoom <= 0 || !transform.panX.isFinite() || !transform.panY.isFinite()) return null
        val scale = minOf(viewWidth.toDouble() / frameWidth, viewHeight.toDouble() / frameHeight) * transform.zoom
        val width = frameWidth * scale
        val height = frameHeight * scale
        val left = (viewWidth - width) / 2 + transform.panX
        val top = (viewHeight - height) / 2 + transform.panY
        val normalizedX = (x - left) / width
        val normalizedY = (y - top) / height
        if (normalizedX !in 0.0..1.0 || normalizedY !in 0.0..1.0) return null
        return ScreenPoint(normalizedX, normalizedY)
    }
    fun clamp(viewWidth: Int, viewHeight: Int, frameWidth: Int, frameHeight: Int, transform: ViewportTransform): ViewportTransform {
        if (viewWidth <= 0 || viewHeight <= 0 || frameWidth <= 0 || frameHeight <= 0) return ViewportTransform()
        val zoom = if (transform.zoom.isFinite()) transform.zoom.coerceIn(1f, 6f) else 1f
        val scale = minOf(viewWidth.toFloat() / frameWidth, viewHeight.toFloat() / frameHeight) * zoom
        val maxX = ((frameWidth * scale - viewWidth) / 2).coerceAtLeast(0f)
        val maxY = ((frameHeight * scale - viewHeight) / 2).coerceAtLeast(0f)
        return ViewportTransform(zoom, transform.panX.takeIf { it.isFinite() }?.coerceIn(-maxX, maxX) ?: 0f, transform.panY.takeIf { it.isFinite() }?.coerceIn(-maxY, maxY) ?: 0f)
    }
    fun fill(viewWidth: Int, viewHeight: Int, frameWidth: Int, frameHeight: Int): ViewportTransform {
        if (viewWidth <= 0 || viewHeight <= 0 || frameWidth <= 0 || frameHeight <= 0) return ViewportTransform()
        val fit = minOf(viewWidth.toFloat() / frameWidth, viewHeight.toFloat() / frameHeight)
        val cover = maxOf(viewWidth.toFloat() / frameWidth, viewHeight.toFloat() / frameHeight)
        return ViewportTransform((cover / fit).coerceIn(1f, 6f))
    }
    fun zoomAround(viewWidth: Int, viewHeight: Int, frameWidth: Int, frameHeight: Int, old: ViewportTransform,
                   factor: Float, centerX: Float, centerY: Float, dragX: Float = 0f, dragY: Float = 0f): ViewportTransform {
        if (!factor.isFinite() || factor <= 0) return old
        val zoom = (old.zoom * factor).coerceIn(1f, 6f)
        val ratio = zoom / old.zoom
        val x = centerX - viewWidth / 2f
        val y = centerY - viewHeight / 2f
        return clamp(viewWidth, viewHeight, frameWidth, frameHeight, ViewportTransform(zoom,
            x - (x - old.panX) * ratio + dragX, y - (y - old.panY) * ratio + dragY))
    }
}
