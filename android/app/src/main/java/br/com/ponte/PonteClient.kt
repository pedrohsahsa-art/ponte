package br.com.ponte

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.buffer
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.launch
import kotlinx.coroutines.delay
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okhttp3.Protocol
import okio.BufferedSink
import okio.ByteString
import okio.ByteString.Companion.toByteString
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.security.SecureRandom
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManager

data class RemoteFile(val id: String, val name: String, val size: Long, val source: String)
class PonteHttpException(val status: Int, message: String) : IOException(message)
data class StreamFrame(val jpeg: ByteArray, val monitorId: String = "", val acknowledge: () -> Unit)
data class PcStatus(val name: String, val version: String, val fileSessionId: String = "")

class PonteClient(private val connection: Connection) {
    fun shareAndroid(listener: WebSocketListener): WebSocket = streamClient.newWebSocket(request("/api/android/stream").build(), listener)
    private val trust = PinnedTrustManager(connection.fingerprint)
    private val client = OkHttpClient.Builder()
        .sslSocketFactory(SSLContext.getInstance("TLS").apply { init(null, arrayOf<TrustManager>(trust), SecureRandom()) }.socketFactory, trust)
        // A private, self-signed identity is checked by exact SHA-256 certificate pin above.
        .hostnameVerifier { hostname, _ -> hostname == NetworkHost.canonical(connection.host) }
        .followRedirects(false).followSslRedirects(false)
        .connectTimeout(15, TimeUnit.SECONDS).readTimeout(90, TimeUnit.SECONDS)
        .writeTimeout(90, TimeUnit.SECONDS).callTimeout(0, TimeUnit.SECONDS).build()
    private val remoteClient = client.newBuilder()
        .connectTimeout(3, TimeUnit.SECONDS).readTimeout(5, TimeUnit.SECONDS).writeTimeout(5, TimeUnit.SECONDS)
        .callTimeout(8, TimeUnit.SECONDS).retryOnConnectionFailure(false).build()
    private val streamClient = remoteClient.newBuilder().protocols(listOf(Protocol.HTTP_1_1))
        .readTimeout(10, TimeUnit.SECONDS).callTimeout(0, TimeUnit.SECONDS)
        .pingInterval(10, TimeUnit.SECONDS).build()
    private val pairClient = client.newBuilder().connectTimeout(4, TimeUnit.SECONDS).writeTimeout(8, TimeUnit.SECONDS)
        .readTimeout(75, TimeUnit.SECONDS).callTimeout(80, TimeUnit.SECONDS).retryOnConnectionFailure(false).build()

    private fun request(path: String): Request.Builder = Request.Builder().url(NetworkHost.url(connection.host, connection.port, path.substringBefore('?')).newBuilder().encodedQuery(path.substringAfter('?', "").ifEmpty { null }).build())
        .apply { if (connection.token.isNotEmpty()) header("Authorization", "Bearer ${connection.token}") }

    private fun check(response: Response): Response {
        if (!response.isSuccessful) {
            val message = when (response.code) {
                401 -> "Acesso inválido ou revogado. Gere um novo QR Code no PC."
                403 -> "Acesso não autorizado no PC. Para controlar a tela, selecione este telefone e autorize o controle no Windows."
                404 -> "Recurso não encontrado. Confira se o Ponte do Windows está atualizado."
                409 -> "A conexão da tela foi substituída. Reconectando…"
                423 -> "Área de trabalho temporariamente indisponível. A autorização continua válida."
                413 -> "O servidor recusou o tamanho desta requisição."
                429, 503 -> "Muitas tentativas. Aguarde um minuto e gere outro QR Code."
                507 -> "O PC não conseguiu gravar o arquivo. Verifique o espaço em disco."
                else -> "O PC respondeu com erro ${response.code}."
            }
            response.close()
            throw PonteHttpException(response.code, message)
        }
        return response
    }
    suspend fun openRemote(): RemoteSession = withContext(Dispatchers.IO) {
        check(remoteClient.newCall(request("/api/remote/session").post(ByteArray(0).toRequestBody()).build()).execute()).use { response ->
            val json = JSONObject(response.body!!.string())
            val id = json.getString("sessionId")
            require(id.matches(Regex("[0-9a-fA-F]{64}"))) { "Sessão de controle inválida." }
            RemoteSession(id, json.optLong("intervalMs", 50).coerceIn(20, 2000))
        }
    }
    suspend fun pcStatus(): PcStatus = withContext(Dispatchers.IO) {
        check(remoteClient.newCall(request("/api/status").build()).execute()).use { response ->
            val json = JSONObject(response.body!!.string())
            PcStatus(json.getString("pcName"), json.getString("version"), json.optString("fileSessionId"))
        }
    }
    suspend fun keyboardTarget(session: String, x: Double, y: Double): KeyboardTarget = withContext(Dispatchers.IO) {
        val path = "/api/remote/keyboard?x=$x&y=$y"
        check(remoteClient.newCall(request(path).header("X-Ponte-Session", session).build()).execute()).use { response ->
            val json = JSONObject(response.body!!.string())
            KeyboardTarget(json.getBoolean("editable"), json.optBoolean("password"), json.optString("focusId"), json.optBoolean("available", true))
        }
    }
    suspend fun desktopOptions(session: String): DesktopOptions = withContext(Dispatchers.IO) {
        check(remoteClient.newCall(request("/api/remote/options").header("X-Ponte-Session", session).build()).execute()).use {
            val json = JSONObject(it.body!!.string())
            val list = json.getJSONArray("monitors")
            val monitors = (0 until list.length()).map { index ->
                val m = list.getJSONObject(index)
                DesktopMonitor(m.getString("id"), m.getString("name"), m.getInt("width"), m.getInt("height"), m.getBoolean("primary"))
            }
            val settings = json.getJSONObject("settings")
            DesktopOptions(monitors, settings.getString("monitorId"), settings.getString("profile"), json.getBoolean("audioAllowed"))
        }
    }
    suspend fun configureDesktop(session: String, monitor: String, profile: String) = withContext(Dispatchers.IO) {
        val body = JSONObject().put("monitorId", monitor).put("profile", profile).toString().toRequestBody("application/json".toMediaType())
        check(remoteClient.newCall(request("/api/remote/options").header("X-Ponte-Session", session).post(body).build()).execute()).close()
    }
    suspend fun wakeTargets(): List<WakeTarget> = withContext(Dispatchers.IO) {
        check(remoteClient.newCall(request("/api/network/wake").build()).execute()).use {
            val list = JSONArray(it.body!!.string())
            (0 until list.length()).map { index -> val j = list.getJSONObject(index); WakeTarget(j.getString("mac"), j.getString("broadcast")) }
        }
    }
    suspend fun internetAddress(): String? = withContext(Dispatchers.IO) {
        check(remoteClient.newCall(request("/api/network/internet").build()).execute()).use {
            val json = JSONObject(it.body!!.string())
            if (!json.optBoolean("enabled")) return@withContext null
            json.optString("hostname").takeIf { host -> host.isNotEmpty() && host != "null" }?.let { host -> return@withContext NetworkHost.internet(host) }
            json.optJSONArray("ipv6")?.optString(0)?.takeIf { address -> address.isNotEmpty() }?.let(NetworkHost::internet)
        }
    }
    fun remoteAudio(session: String) = callbackFlow {
        var rate = 0
        val socket = streamClient.newWebSocket(request("/api/remote/audio").header("X-Ponte-Session", session).build(), object : WebSocketListener() {
            override fun onMessage(webSocket: WebSocket, text: String) {
                try {
                    val json = JSONObject(text)
                    require(json.getInt("channels") == 2 && json.getInt("bits") == 16)
                    rate = json.getInt("sampleRate")
                    require(rate in 8000..192000)
                } catch (e: Exception) { close(e); webSocket.cancel() }
            }
            override fun onMessage(webSocket: WebSocket, bytes: ByteString) {
                if (rate == 0 || bytes.size > 262144 || bytes.size % 4 != 0 || !trySend(AudioPacket(bytes.toByteArray(), rate)).isSuccess) {
                    close(IOException("Áudio interrompido: receptor lento ou formato inválido.")); webSocket.cancel()
                }
            }
            override fun onClosing(webSocket: WebSocket, code: Int, reason: String) { webSocket.close(code, reason); close(IOException("Áudio encerrado.")) }
            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                val status = response?.code; response?.close()
                close(if (status != null) PonteHttpException(status, "Áudio indisponível. Confira a permissão de áudio no Windows.") else IOException("Conexão de áudio interrompida.", t))
            }
        })
        awaitClose { socket.cancel() }
    }.buffer(6)
    fun remoteFrames(session: String) = callbackFlow {
        // Bounds only startup, not the lifetime of a healthy video stream.
        val firstFrameDeadline = launch {
            delay(10000)
            close(java.net.SocketTimeoutException("O primeiro quadro da transmissão não chegou em 10 segundos."))
        }
        val socket = streamClient.newWebSocket(request("/api/remote/stream").header("X-Ponte-Session", session).build(), object : WebSocketListener() {
            private var monitorId = ""
            override fun onMessage(webSocket: WebSocket, text: String) {
                try { monitorId = JSONObject(text).getString("monitorId") }
                catch (e: Exception) { close(IOException("Metadados de tela inválidos.", e)); webSocket.cancel() }
            }
            override fun onMessage(webSocket: WebSocket, bytes: ByteString) {
                firstFrameDeadline.cancel()
                if (bytes.size > 4 * 1024 * 1024) { close(IOException("Quadro de tela acima do limite.")); webSocket.cancel(); return }
                trySend(StreamFrame(bytes.toByteArray(), monitorId) {
                    if (!webSocket.send(byteArrayOf(1).toByteString())) throw IOException("Conexão da tela interrompida.")
                })
            }
            override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
                webSocket.close(code, reason)
                close(IOException(if (reason == "desktop_unavailable") "Windows bloqueado ou área de trabalho indisponível. Aguardando…" else "Conexão da tela interrompida. Reconectando…"))
            }
            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) { close(IOException("Conexão da tela encerrada.")) }
            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                val error = if (response != null) PonteHttpException(response.code, when (response.code) {
                    401 -> "Pareamento inválido ou revogado. Faça novo pareamento."
                    403 -> "Aguardando você autorizar este telefone no PC."
                    404 -> "Atualize também o Ponte do Windows para usar a transmissão contínua."
                    423 -> "Aguardando a área de trabalho do Windows ficar disponível."
                    else -> "Falha ao abrir a transmissão (${response.code})."
                }) else t
                response?.close()
                close(error)
            }
        })
        awaitClose { firstFrameDeadline.cancel(); socket.cancel() }
    }.buffer(1, BufferOverflow.DROP_OLDEST)
    suspend fun remoteFrame(session: String): ByteArray = withContext(Dispatchers.IO) {
        check(remoteClient.newCall(request("/api/remote/frame").header("X-Ponte-Session", session).build()).execute()).use { response ->
            require(response.body!!.contentType()?.toString()?.startsWith("image/jpeg") == true) { "Formato de tela inválido." }
            val output = java.io.ByteArrayOutputStream()
            response.body!!.byteStream().use { input ->
                val buffer = ByteArray(16384)
                while (true) {
                    currentCoroutineContext().ensureActive()
                    val count = input.read(buffer)
                    if (count < 0) break
                    require(output.size() + count <= 2 * 1024 * 1024) { "Quadro de tela acima do limite." }
                    output.write(buffer, 0, count)
                }
            }
            output.toByteArray()
        }
    }
    suspend fun sendRemote(session: String, command: RemoteCommand) = withContext(Dispatchers.IO) {
        val json = JSONObject().put("kind", command.kind).put("x", command.x).put("y", command.y).put("delta", command.delta)
        command.text?.let { json.put("text", it) }
        command.key?.let { json.put("key", it) }
        val body = json.toString().toRequestBody("application/json".toMediaType())
        check(remoteClient.newCall(request("/api/remote/input").header("X-Ponte-Session", session).post(body).build()).execute()).close()
    }
    suspend fun closeRemote(session: String) = withContext(Dispatchers.IO) {
        check(remoteClient.newCall(request("/api/remote/session").header("X-Ponte-Session", session).delete().build()).execute()).close()
    }
    fun cancelRemoteRequests() { remoteClient.dispatcher.cancelAll() }
    suspend fun pair(code: String, name: String): Connection = withContext(Dispatchers.IO) {
        val json = JSONObject().put("code", code).put("name", name)
        val body = json.toString().toRequestBody("application/json".toMediaType())
        check(pairClient.newCall(request("/pair").post(body).build()).execute()).use { response ->
            val result = JSONObject(response.body!!.string())
            connection.copy(token = result.getString("token"), pcName = result.getString("pcName"))
        }
    }
    suspend fun listFiles(): List<RemoteFile> = withContext(Dispatchers.IO) {
        check(remoteClient.newCall(request("/api/files").build()).execute()).use { response ->
            val list = JSONArray(response.body!!.string())
            (0 until list.length()).map { index ->
                val item = list.getJSONObject(index)
                RemoteFile(item.getString("id"), item.getString("name"), item.getLong("size"), item.getString("source"))
            }
        }
    }
    suspend fun upload(name: String, size: Long, open: () -> InputStream) = withContext(Dispatchers.IO) {
        val context = currentCoroutineContext()
        val body = object : RequestBody() {
            override fun contentType() = "application/octet-stream".toMediaType()
            override fun contentLength() = size
            override fun isOneShot() = true
            override fun writeTo(sink: BufferedSink) {
                open().use { input ->
                    val buffer = ByteArray(65536)
                    var total = 0L
                    while (true) {
                        context.ensureActive()
                        val count = input.read(buffer)
                        if (count < 0) break
                        total += count
                        sink.write(buffer, 0, count)
                    }
                }
            }
        }
        val url = okhttp3.HttpUrl.Builder().scheme("https").host(connection.host).port(connection.port)
            .addPathSegments("api/files").addQueryParameter("name", name).build()
        check(client.newCall(request("/api/files").url(url).post(body).build()).execute()).close()
    }
    suspend fun download(file: RemoteFile, open: () -> OutputStream) = withContext(Dispatchers.IO) {
        require(file.id.matches(Regex("[a-fA-F0-9]{32}"))) { "Identificador de arquivo inválido." }
        check(client.newCall(request("/api/files/${file.id}").build()).execute()).use { response ->
            response.body!!.byteStream().use { input ->
                open().use { output ->
                    val buffer = ByteArray(65536)
                    var total = 0L
                    while (true) {
                        currentCoroutineContext().ensureActive()
                        val count = input.read(buffer)
                        if (count < 0) break
                        total += count
                        output.write(buffer, 0, count)
                    }
                    if (total != file.size) throw IOException("Download incompleto. Tente novamente.")
                }
            }
        }
    }
}
