package br.com.ponte

data class DesktopMonitor(val id: String, val name: String, val width: Int, val height: Int, val primary: Boolean)
data class DesktopOptions(val monitors: List<DesktopMonitor>, val monitorId: String, val profile: String, val audioAllowed: Boolean)
data class AudioPacket(val pcm: ByteArray, val sampleRate: Int)

// Hysteresis: downgrade after a slow window; upgrade only after three good windows.
class AdaptiveQuality {
    private var good = 0
    fun next(profile: String, fps: Int): String {
        if (fps < 10) { good = 0; return if (profile == "sharp") "balanced" else "fluid" }
        if (fps >= 14) good++ else good = 0
        if (good < 3) return profile
        good = 0
        return if (profile == "fluid") "balanced" else "sharp"
    }
}
