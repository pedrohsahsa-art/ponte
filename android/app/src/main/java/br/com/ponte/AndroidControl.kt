package br.com.ponte

import org.json.JSONObject

data class AndroidControl(val kind: String, val x: Double = 0.0, val y: Double = 0.0, val endX: Double = 0.0, val endY: Double = 0.0, val text: String = "") {
    val valid: Boolean get() = when (kind) {
        "ack", "back", "home", "recents" -> true
        "tap" -> coordinate(x) && coordinate(y)
        "swipe" -> coordinate(x) && coordinate(y) && coordinate(endX) && coordinate(endY)
        "text" -> text.length in 1..1000 && text.none { it.isISOControl() && it != '\n' }
        else -> false
    }
    companion object {
        private fun coordinate(value: Double) = value.isFinite() && value in 0.0..1.0
        fun parse(value: String): AndroidControl {
            require(value.length <= 8192)
            val json = JSONObject(value)
            return AndroidControl(json.getString("kind"), json.optDouble("x", 0.0), json.optDouble("y", 0.0), json.optDouble("endX", 0.0), json.optDouble("endY", 0.0), json.optString("text", "")).also { require(it.valid) }
        }
        fun protectedPackage(name: String): Boolean = name == "br.com.ponte" || name == "com.android.systemui" || name == "com.android.settings" || name.contains("permissioncontroller") || name.contains("packageinstaller")
    }
}
