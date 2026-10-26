package br.com.ponte

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.*
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.collect
import kotlinx.coroutines.flow.onCompletion

class RemoteViewModel(application: Application) : AndroidViewModel(application) {
    var connectionRoute by mutableStateOf("Rede local"); private set
    var monitors by mutableStateOf<List<DesktopMonitor>>(emptyList()); private set
    var monitorId by mutableStateOf(""); private set
    var profile by mutableStateOf("balanced"); private set
    var automaticQuality by mutableStateOf(true); private set
    var dragMode by mutableStateOf(false); private set
    var audioEnabled by mutableStateOf(false); private set
    var monitorReady by mutableStateOf(true); private set
    private var audioJob: Job? = null
    private var audioGeneration = 0
    private var audioRequested = false
    private var foreground = false
    private val networkWatcher = NetworkWatcher(application) {
        viewModelScope.launch { if (wanted && foreground) { pause(); message = "Rede alterada. Restabelecendo a conexão segura…"; resume() } }
    }
    private var dragging = false
    private var dragPoint = ScreenPoint(0.0, 0.0)
    private var dragHeartbeat: Job? = null
    private val adaptive = AdaptiveQuality()
    var frame by mutableStateOf<Bitmap?>(null); private set
    var frameWidth by mutableStateOf(1920); private set
    var frameHeight by mutableStateOf(1080); private set
    var fps by mutableStateOf(0); private set
    var connected by mutableStateOf(false); private set
    var connecting by mutableStateOf(false); private set
    var message by mutableStateOf("Conectando à tela…"); private set
    var phase by mutableStateOf(""); private set
    var attempts by mutableStateOf(0); private set
    var keyboardOpenRequest by mutableStateOf(0); private set
    var keyboardHideRequest by mutableStateOf(0); private set
    var keyboardResetRequest by mutableStateOf(0); private set
    var keyboardPassword by mutableStateOf(false); private set
    private data class QueuedInput(val commands: List<RemoteCommand>, val clickSequence: Int? = null, val settings: Pair<String, String>? = null)
    private var keyboardProbeSequence = 0
    private var keyboardEventSequence = 0
    private var wanted = false
    private var screenOpened = false
    private var connection: Connection? = null
    private var job: Job? = null
    private var api: PonteClient? = null
    private var session: String? = null
    private var commands: Channel<QueuedInput>? = null
    private var pendingMove: RemoteCommand? = null
    private var generation = 0

    fun enter(value: Connection) {
        if (!screenOpened || connection != value) { screenOpened = true; connection = value; wanted = true }
        resume()
    }
    fun start(value: Connection) { connection = value; wanted = true; resume() }
    fun resume() {
        foreground = true
        var target = connection ?: return
        if (!wanted || job?.isActive == true) return
        val currentGeneration = ++generation
        connecting = true
        attempts = 0
        message = "Conectando a ${target.host}:${target.port}…"
        job = viewModelScope.launch {
            var failures = 0
            while (isActive && wanted && generation == currentGeneration) {
                var client = PonteClient(target)
                api = client
                val queue = Channel<QueuedInput>(64)
                commands = queue
                var opened: String? = null
                try {
                    val preferLocal = attempts == 0
                    attempts++
                    val resolved = ConnectionRouting.resolve(target, preferLocal = preferLocal)
                    if (resolved != target) {
                        target = resolved; connection = resolved
                        withContext(Dispatchers.IO) { SecureConnectionStore(getApplication()).save(resolved) }
                        client = PonteClient(target); api = client
                    }
                    connectionRoute = target.routeLabel
                    if (connectionRoute == "Internet direta" && preferLocal && automaticQuality) profile = "fluid"
                    phase = "1/3 · verificando o PC"
                    client.pcStatus()
                    phase = "2/3 · verificando a autorização de controle"
                    val id = client.openRemote().sessionId
                    opened = id
                    ensureActive()
                    session = opened
                    val options = client.desktopOptions(id)
                    monitors = options.monitors
                    if (monitorId.isNotEmpty() && monitors.none { it.id == monitorId }) monitorId = ""
                    client.configureDesktop(id, monitorId, profile)
                    monitorReady = false
                    phase = "3/3 · aguardando o primeiro quadro (até 10 s)"
                    coroutineScope {
                        launch {
                            while (isActive) {
                                val item = queue.tryReceive().getOrNull() ?: pendingMove?.let { pendingMove = null; QueuedInput(listOf(it)) }
                                if (item == null) { delay(12); continue }
                                item.settings?.let { (monitor, quality) -> client.configureDesktop(id, monitor, quality); profile = quality }
                                var accepted = true
                                for (command in item.commands) {
                                    while (isActive) {
                                        try { client.sendRemote(id, command); break }
                                        catch (e: PonteHttpException) {
                                            if (e.status == 429) { delay(150); continue } // explicit rejection: safe to retry, unlike a lost response
                                            if (e.status !in setOf(400, 423)) throw e
                                            accepted = false
                                            if (command.kind == "edit") dismissKeyboard()
                                            message = if (e.status == 423) "Esta janela não aceitou a entrada. A transmissão continua." else "Entrada não aceita. Confira se o programa Windows está atualizado."
                                            break
                                        }
                                    }
                                    if (!accepted) break
                                }
                                if (accepted && item.clickSequence != null) launch {
                                    inspectKeyboardAfterClick(client, id, item.commands.last(), item.clickSequence, currentGeneration)
                                }
                            }
                        }
                        var measuredAt = System.nanoTime()
                        var frames = 0
                        var qualityAt = System.nanoTime()
                        var qualityFrames = 0
                        client.remoteFrames(id).onCompletion {
                            // Abort any blocked input call BEFORE coroutineScope waits
                            // for its child, so a dropped stream cannot freeze retry for 8 s.
                            client.cancelRemoteRequests()
                         }.collect { packet ->
                            if (monitorId.isNotEmpty() && packet.monitorId != monitorId) { packet.acknowledge(); return@collect }
                            val bitmap = withContext(Dispatchers.Default) {
                                val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                                BitmapFactory.decodeByteArray(packet.jpeg, 0, packet.jpeg.size, bounds)
                                require(bounds.outWidth in 1..1920 && bounds.outHeight in 1..1080) { "Resolução de tela inválida." }
                                BitmapFactory.decodeByteArray(packet.jpeg, 0, packet.jpeg.size, BitmapFactory.Options().apply { inPreferredConfig = Bitmap.Config.RGB_565 })
                                    ?: error("Não foi possível decodificar a tela.")
                            }
                            ensureActive()
                            frame = bitmap
                            frameWidth = bitmap.width
                            frameHeight = bitmap.height
                            monitorReady = true
                            if (!connected) {
                                message = "Ao vivo · toque para clicar · pinça para ampliar"
                            }
                            val firstConnection = !connected
                            connected = true
                            if (firstConnection && audioRequested && !audioEnabled) startAudio()
                            connecting = false
                            phase = "Transmitindo"
                            failures = 0
                            frames++
                            qualityFrames++
                            val elapsed = System.nanoTime() - measuredAt
                            if (elapsed >= 1_000_000_000) {
                                fps = (frames * 1_000_000_000L / elapsed).toInt()
                                frames = 0
                                measuredAt = System.nanoTime()
                            }
                            packet.acknowledge() // backpressure: decode before requesting the next frame
                            val qualityElapsed = System.nanoTime() - qualityAt
                            if (automaticQuality && qualityElapsed >= 4_000_000_000L && !dragging) {
                                val next = adaptive.next(profile, (qualityFrames * 1_000_000_000L / qualityElapsed).toInt())
                                qualityAt = System.nanoTime(); qualityFrames = 0
                                if (next != profile) queue.trySend(QueuedInput(emptyList(), settings = monitorId to next))
                            }
                        }
                        throw java.io.IOException("Transmissão encerrada.")
                    }
                } catch (e: CancellationException) { throw e }
                catch (e: Exception) {
                    if (!RemoteRetry.shouldRetry(e)) {
                        wanted = false
                        message = ConnectionDiagnostics.describe(e, target.host, target.port)
                        phase = "Conexão parada"
                    } else {
                        failures++
                        if (failures % 2 == 0 && e !is PonteHttpException) {
                            phase = "Procurando o PC na rede local…"
                            val recovered = LanNetwork.recover(target)
                            if (recovered != null) {
                                target = recovered; connection = recovered
                                withContext(Dispatchers.IO) { SecureConnectionStore(getApplication()).save(recovered) }
                            }
                        }
                        message = ConnectionDiagnostics.describe(e, target.host, target.port)
                        phase = "Nova tentativa em ${RemoteRetry.delayMs(failures) / 1000.0} s"
                    }
                } finally {
                    queue.close()
                    client.cancelRemoteRequests()
                    if (generation == currentGeneration) {
                        commands = null
                        pendingMove = null
                        session = null
                        frame = null
                        connected = false
                        stopAudio(); clearDrag()
                        dismissKeyboard()
                        fps = 0
                        connecting = wanted
                    }
                    if (opened != null) closeLater(client, opened)
                }
                if (wanted && isActive) delay(RemoteRetry.delayMs(failures))
            }
            if (generation == currentGeneration) connecting = false
        }
    }
    fun move(point: ScreenPoint) { if (connected && monitorReady) { dragPoint = point; pendingMove = RemoteCommand("move", point.x, point.y) } }
    fun toggleDragMode() { endDrag(); dragMode = !dragMode; dismissKeyboard() }
    fun beginDrag(point: ScreenPoint) {
        if (!connected || !monitorReady) return
        dismissKeyboard()
        dragPoint = point
        dragging = send(RemoteCommand("mousedown", point.x, point.y))
        if (dragging) dragHeartbeat = viewModelScope.launch { while (isActive && dragging) { delay(400); send(RemoteCommand("dragkeepalive")) } }
    }
    fun endDrag() {
        if (dragging) {
            val move = pendingMove
            pendingMove = null
            enqueue(QueuedInput(listOfNotNull(move, RemoteCommand("mouseup", dragPoint.x, dragPoint.y))))
        }
        clearDrag()
    }
    private fun clearDrag() { dragging = false; dragHeartbeat?.cancel(); dragHeartbeat = null }
    fun selectMonitor(id: String) {
        if (!connected || id == monitorId) return
        endDrag(); dismissKeyboard(); pendingMove = null
        while (commands?.tryReceive()?.isSuccess == true) { }
        monitorId = id; monitorReady = false; frame = null
        commands?.trySend(QueuedInput(emptyList(), settings = id to profile))
    }
    fun chooseQuality(value: String) {
        automaticQuality = value == "auto"
        if (value != "auto" && connected) commands?.trySend(QueuedInput(emptyList(), settings = monitorId to value))
    }
    fun toggleAudio() {
        if (audioEnabled) { audioRequested = false; stopAudio(); return }
        audioRequested = true
        startAudio()
    }
    private fun startAudio() {
        val client = api ?: return
        val id = session ?: return
        if (!connected) return
        audioEnabled = true
        val run = ++audioGeneration
        audioJob = viewModelScope.launch {
            var track: android.media.AudioTrack? = null
            try {
                client.remoteAudio(id).collect { packet ->
                    withContext(Dispatchers.IO) {
                        val output = track ?: android.media.AudioTrack.Builder()
                            .setAudioAttributes(android.media.AudioAttributes.Builder().setUsage(android.media.AudioAttributes.USAGE_MEDIA).setContentType(android.media.AudioAttributes.CONTENT_TYPE_MOVIE).build())
                            .setAudioFormat(android.media.AudioFormat.Builder().setEncoding(android.media.AudioFormat.ENCODING_PCM_16BIT).setSampleRate(packet.sampleRate).setChannelMask(android.media.AudioFormat.CHANNEL_OUT_STEREO).build())
                            .setBufferSizeInBytes(maxOf(android.media.AudioTrack.getMinBufferSize(packet.sampleRate, android.media.AudioFormat.CHANNEL_OUT_STEREO, android.media.AudioFormat.ENCODING_PCM_16BIT), packet.sampleRate / 5 * 4))
                            .setTransferMode(android.media.AudioTrack.MODE_STREAM).build().also { track = it; it.play() }
                        check(output.write(packet.pcm, 0, packet.pcm.size, android.media.AudioTrack.WRITE_BLOCKING) == packet.pcm.size) { "Falha na saída de áudio." }
                    }
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                if (audioGeneration == run && (e is IllegalArgumentException || e is PonteHttpException && e.status in setOf(400, 401, 403, 404))) audioRequested = false
                message = e.message ?: "Áudio indisponível; permita no Windows."
            }
            finally {
                track?.let { output -> try { if (output.state == android.media.AudioTrack.STATE_INITIALIZED) { output.pause(); output.flush() } } finally { output.release() } }
                if (audioGeneration == run) audioEnabled = false
            }
        }
    }
    private fun stopAudio() { audioGeneration++; audioJob?.cancel(); audioJob = null; audioEnabled = false }
    fun click(point: ScreenPoint, kind: String = "click"): Boolean {
        if (!connected || !monitorReady) return false
        val sequence = ++keyboardProbeSequence
        keyboardResetRequest++
        if (kind == "rightclick") keyboardHideRequest = ++keyboardEventSequence
        return enqueue(QueuedInput(listOf(RemoteCommand(kind, point.x, point.y)), if (kind == "rightclick") null else sequence))
    }
    fun key(key: String): Boolean { keyboardResetRequest++; return send(RemoteCommand("key", key = key)) }
    fun scroll(delta: Int) = send(RemoteCommand("scroll", delta = delta))
    fun text(value: String): Boolean {
        if (value.isEmpty() || value.length > 500 || value.any { it.isISOControl() }) {
            message = "Envie de 1 a 500 caracteres por vez. Use Enter para mudar de linha."
            return false
        }
        return send(RemoteCommand("text", text = value))
    }
    private fun send(command: RemoteCommand): Boolean {
        return enqueue(QueuedInput(listOf(command)))
    }
    fun edit(change: KeyboardEdit): Boolean {
        if (change.text.any { it.isISOControl() && it != '\n' && it != '\t' }) return false
        val batch = KeyboardEdits.commands(change)
        if (batch.isEmpty()) return true
        return enqueue(QueuedInput(batch))
    }
    private fun enqueue(item: QueuedInput): Boolean {
        if (!connected || !monitorReady) return false
        val move = pendingMove
        if (move != null && dragging) commands?.trySend(QueuedInput(listOf(move)))
        pendingMove = null
        return (commands?.trySend(item)?.isSuccess == true).also {
            if (!it) { message = "Fila de entrada ocupada. Aguarde e toque novamente no campo do PC."; dismissKeyboard() }
        }
    }
    fun requestKeyboard() {
        if (!connected) return
        keyboardProbeSequence++
        keyboardPassword = true // manual fallback cannot know whether the remote field is secret
        keyboardOpenRequest = ++keyboardEventSequence
    }
    fun dismissKeyboard() { keyboardProbeSequence++; keyboardHideRequest = ++keyboardEventSequence }
    private suspend fun inspectKeyboardAfterClick(client: PonteClient, id: String, click: RemoteCommand, sequence: Int, run: Int) {
        try {
            repeat(4) {
                delay(90)
                if (sequence != keyboardProbeSequence || generation != run || session != id) return
                val target = client.keyboardTarget(id, click.x, click.y)
                if (sequence != keyboardProbeSequence || generation != run || session != id) return
                if (target.editable) {
                    keyboardPassword = target.password
                    keyboardOpenRequest = ++keyboardEventSequence
                    return
                }
            }
            if (sequence == keyboardProbeSequence) keyboardHideRequest = ++keyboardEventSequence
        } catch (e: CancellationException) { throw e }
        catch (_: Exception) {
            // Optional accessibility support must not disconnect the video/input.
            if (sequence == keyboardProbeSequence) message = "Teclado automático indisponível neste programa; use o botão Teclado."
        }
    }
    private fun closeLater(client: PonteClient, id: String) { viewModelScope.launch { runCatching { client.closeRemote(id) } } }
    fun pause() {
        foreground = false
        endDrag(); stopAudio()
        generation++
        val oldClient = api
        val oldSession = session
        job?.cancel()
        job = null
        oldClient?.cancelRemoteRequests()
        commands?.close()
        commands = null
        pendingMove = null
        session = null
        api = null
        connected = false
        dismissKeyboard()
        connecting = false
        frame = null
        fps = 0
        phase = "Conexão pausada"
        if (oldClient != null && oldSession != null) closeLater(oldClient, oldSession)
    }
    fun stop() { wanted = false; audioRequested = false; pause(); phase = "Desconectado"; message = "Sessão encerrada. Sua autorização no PC continua válida." }
    fun leave() { stop(); screenOpened = false }
    override fun onCleared() { networkWatcher.close(); wanted = false; pause(); super.onCleared() }
}
