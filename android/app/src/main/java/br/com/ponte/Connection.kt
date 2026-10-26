package br.com.ponte

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONObject
import java.security.KeyStore
import java.net.URI
import java.net.URLDecoder
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

data class Connection(val host: String, val port: Int, val fingerprint: String, val token: String = "", val pcName: String = "Meu computador", val wakeTargets: List<WakeTarget> = emptyList(), val localHost: String = host, val internetHost: String = "", val learnInternet: Boolean = true) {
    val routeLabel: String get() = if (internetHost.isNotEmpty() && host == internetHost) "Internet direta" else "Rede local"
    fun json() = JSONObject().put("host", host).put("port", port).put("fingerprint", fingerprint).put("token", token).put("pcName", pcName)
        .put("wakeTargets", org.json.JSONArray().apply { wakeTargets.forEach { put(JSONObject().put("mac", it.mac).put("broadcast", it.broadcast)) } })
        .put("localHost", localHost).put("internetHost", internetHost).put("learnInternet", learnInternet)
    companion object {
        fun validHost(host: String): Boolean {
            val parts = host.split('.')
            return parts.size == 4 && parts.all { it.isNotEmpty() && it.all(Char::isDigit) && (it.toIntOrNull() ?: -1) in 0..255 }
        }
        fun fromJson(json: JSONObject): Connection {
            val targets = json.optJSONArray("wakeTargets") ?: org.json.JSONArray()
            return Connection(json.getString("host"), json.getInt("port"), json.getString("fingerprint"), json.optString("token"), json.optString("pcName", "Meu computador"),
                (0 until targets.length()).map { val t = targets.getJSONObject(it); WakeTarget(t.getString("mac"), t.getString("broadcast")) },
                json.optString("localHost", json.getString("host")), json.optString("internetHost"), json.optBoolean("learnInternet", true))
        }
        fun parsePairing(text: String): Pair<Connection, String> {
            val uri = URI(text.trim())
            require(uri.scheme == "ponte" && uri.rawAuthority == "pair" && uri.path.isNullOrEmpty() && uri.fragment == null) { "Este não é um QR Code do Ponte." }
            val entries = (uri.rawQuery ?: "").split('&').map { entry ->
                val parts = entry.split('=', limit = 2)
                parts[0] to URLDecoder.decode(parts.getOrElse(1) { "" }, "UTF-8")
            }
            require(entries.map { it.first }.distinct().size == entries.size) { "Link com parâmetros duplicados." }
            val params = entries.toMap()
            val host = params["host"] ?: ""
            val port = params["port"]?.toIntOrNull() ?: 0
            val fingerprint = params["fingerprint"] ?: ""
            val code = params["code"] ?: ""
            require(validHost(host) && port in 1..65535 && fingerprint.matches(Regex("[0-9a-fA-F]{64}")) && code.matches(Regex("[0-9a-fA-F]{64}"))) { "Link de pareamento inválido." }
            return Connection(host, port, fingerprint.uppercase()) to code
        }
    }
}

// Android Keystore protects the encryption key; plaintext bearer tokens are never persisted.
class SecureConnectionStore(context: Context) {
    private val prefs = context.getSharedPreferences("ponte_secure", Context.MODE_PRIVATE)
    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey("ponte.connection", null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder("ponte.connection", KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())
        }.generateKey()
    }
    fun save(connection: Connection) {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.ENCRYPT_MODE, key()) }
        val payload = cipher.doFinal(connection.json().toString().toByteArray(Charsets.UTF_8))
        check(prefs.edit().putString("iv", Base64.encodeToString(cipher.iv, Base64.NO_WRAP))
            .putString("payload", Base64.encodeToString(payload, Base64.NO_WRAP)).commit()) { "Não foi possível salvar a conexão." }
    }
    fun load(): Connection? {
        val payload = prefs.getString("payload", null) ?: return null
        val iv = prefs.getString("iv", null) ?: error("Dados de conexão incompletos.")
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply {
            init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, Base64.decode(iv, Base64.NO_WRAP)))
        }
        return Connection.fromJson(JSONObject(String(cipher.doFinal(Base64.decode(payload, Base64.NO_WRAP)), Charsets.UTF_8)))
    }
    fun clear() { check(prefs.edit().clear().commit()) }
}
