package br.com.ponte

import android.app.*
import android.content.*
import android.content.pm.ServiceInfo
import android.graphics.Bitmap
import android.graphics.PixelFormat
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.ImageReader
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.*
import android.util.DisplayMetrics
import android.view.Display
import android.view.WindowManager
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import kotlinx.coroutines.*
import okhttp3.*
import org.json.JSONObject
import okio.ByteString.Companion.toByteString
import java.io.ByteArrayOutputStream
import java.util.concurrent.atomic.AtomicBoolean

class AndroidShareService : Service() {
    companion object {
        @Volatile var authorized = false
        @Volatile var physicalSize: Pair<Int, Int>? = null; private set
        var running by mutableStateOf(false); private set
        var status by mutableStateOf("Compartilhamento parado."); private set
        const val STOP = "br.com.ponte.STOP_SHARING"
    }
    private val main = Handler(Looper.getMainLooper())
    private val thread = HandlerThread("Ponte Android capture")
    private lateinit var capture: Handler
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val closed = AtomicBoolean(false)
    private val waiting = AtomicBoolean(false)
    private var sentAt = 0L
    private var projection: MediaProjection? = null
    private var display: VirtualDisplay? = null
    private var reader: ImageReader? = null
    private var socket: WebSocket? = null
    private var watcher: NetworkWatcher? = null
    private var rotation = 0
    private var width = 0; private var height = 0
    private var cached: ByteArray? = null
    private var lastAvailability = ""
    private fun reportControl(code: String) {
        if (!closed.get()) socket?.send(JSONObject().put("type", "controlStatus").put("code", code).toString())
    }
    private val controlMonitor = object : Runnable {
        override fun run() {
            if (closed.get()) return
            val code = try { PonteAccessibilityService.current?.availability() ?: "accessibility_disabled" } catch (_: Exception) { "window_unavailable" }
            if (code != lastAvailability) { lastAvailability = code; reportControl(code) }
            main.postDelayed(this, 1000)
        }
    }
    private val screenOff = object : BroadcastReceiver() { override fun onReceive(context: Context?, intent: Intent?) { finish("Telefone bloqueado. Compartilhamento encerrado.") } }
    override fun onCreate() {
        super.onCreate(); authorized = false; physicalSize = null; thread.start(); capture = Handler(thread.looper)
        ContextCompat.registerReceiver(this, screenOff, IntentFilter(Intent.ACTION_SCREEN_OFF), ContextCompat.RECEIVER_NOT_EXPORTED)
    }
    override fun onBind(intent: Intent?) = null
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == STOP) { finish("Compartilhamento encerrado pelo telefone."); return START_NOT_STICKY }
        if (running || intent == null) { stopSelf(); return START_NOT_STICKY }
        val notifications = getSystemService(NotificationManager::class.java)
        notifications.createNotificationChannel(NotificationChannel("android_screen", "Compartilhamento Android", NotificationManager.IMPORTANCE_LOW))
        val stop = PendingIntent.getService(this, 1, Intent(this, AndroidShareService::class.java).setAction(STOP), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val notification = NotificationCompat.Builder(this, "android_screen").setSmallIcon(br.com.ponte.R.drawable.ponte_notification).setContentTitle("Ponte · Android compartilhado")
            .setContentText("O PC pareado pode ver sua tela. Pare quando terminar.").setOngoing(true).addAction(0, "Parar", stop).build()
        try {
            if (Build.VERSION.SDK_INT >= 29) startForeground(73, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION) else startForeground(73, notification)
        } catch (_: Exception) { finish("O Android não permitiu iniciar captura em primeiro plano. Abra o Ponte e autorize novamente."); return START_NOT_STICKY }
        running = true; status = "Conectando ao visualizador do PC…"
        try {
            val data = if (Build.VERSION.SDK_INT >= 33) intent.getParcelableExtra("data", Intent::class.java) else @Suppress("DEPRECATION") intent.getParcelableExtra<Intent>("data")
            require(data != null && intent.getIntExtra("result", 0) == Activity.RESULT_OK)
            projection = getSystemService(MediaProjectionManager::class.java).getMediaProjection(Activity.RESULT_OK, data)
            projection!!.registerCallback(object : MediaProjection.Callback() { override fun onStop() { finish("Captura encerrada pelo Android.") } }, main)
            scope.launch {
                try {
                    val saved = withContext(Dispatchers.IO) { SecureConnectionStore(this@AndroidShareService).load() } ?: error("Pareie primeiro com o PC.")
                    val target = ConnectionRouting.resolve(saved, preferLocal = true)
                    socket = PonteClient(target).shareAndroid(object : WebSocketListener() {
                        override fun onOpen(webSocket: WebSocket, response: Response) { main.post { if (closed.get()) webSocket.cancel() else beginCapture() } }
                        override fun onMessage(webSocket: WebSocket, text: String) {
                            try {
                                val command = AndroidControl.parse(text)
                                if (command.kind == "ack") waiting.set(false)
                                else main.post {
                                    if (!closed.get()) {
                                        try {
                                            val service = PonteAccessibilityService.current
                                            if (service == null) reportControl("accessibility_disabled") else service.execute(command, ::reportControl)
                                        } catch (_: Exception) { reportControl("action_rejected") }
                                    }
                                }
                            } catch (_: Exception) { finish("Comando inválido. Compartilhamento encerrado.") }
                        }
                        override fun onClosing(webSocket: WebSocket, code: Int, reason: String) { webSocket.close(code, reason); finish("O PC encerrou a conexão.") }
                        override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) { response?.close(); finish("Conexão indisponível. Abra Android no PC e compartilhe novamente.") }
                    })
                } catch (e: CancellationException) { throw e } catch (_: Exception) { finish("Não foi possível iniciar. Confira o PC e autorize uma nova captura.") }
            }
        } catch (_: Exception) { finish("Não foi possível autorizar a captura. Inicie novamente pelo aplicativo.") }
        return START_NOT_STICKY
    }
    private fun dimensions(): Pair<Int, Int> {
        val manager = getSystemService(WindowManager::class.java)
        return if (Build.VERSION.SDK_INT >= 30) manager.maximumWindowMetrics.bounds.let { it.width() to it.height() }
        else DisplayMetrics().let { @Suppress("DEPRECATION") manager.defaultDisplay.getRealMetrics(it); it.widthPixels to it.heightPixels }
    }
    private fun currentRotation() = getSystemService(DisplayManager::class.java).getDisplay(Display.DEFAULT_DISPLAY)?.rotation ?: 0
    private fun beginCapture() {
        try {
            val size = dimensions(); physicalSize = size; rotation = currentRotation()
            val scale = minOf(1.0, 1280.0 / maxOf(size.first, size.second), 720.0 / minOf(size.first, size.second))
            width = maxOf(2, (size.first * scale).toInt() / 2 * 2); height = maxOf(2, (size.second * scale).toInt() / 2 * 2)
            reader = ImageReader.newInstance(width, height, PixelFormat.RGBA_8888, 2)
            display = projection!!.createVirtualDisplay("Ponte Android", width, height, resources.displayMetrics.densityDpi, DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR, reader!!.surface, null, capture)
            authorized = true; status = "Tela compartilhada. Controle exige Acessibilidade; bloqueio e troca de rede encerram a sessão."
            main.post(controlMonitor)
            watcher = NetworkWatcher(this) { finish("Rede alterada. Autorize uma nova captura para continuar.") }
            capture.post(pump)
        } catch (_: Exception) { finish("Captura indisponível. Autorize novamente no Android.") }
    }
    private val pump = object : Runnable {
        override fun run() {
            if (closed.get()) return
            try {
                if (getSystemService(KeyguardManager::class.java).isDeviceLocked || currentRotation() != rotation) { finish("Bloqueio ou rotação detectados. Inicie uma nova captura."); return }
                if (waiting.get()) {
                    if (SystemClock.elapsedRealtime() - sentAt > 8000) { finish("O PC deixou de confirmar os quadros. Sessão encerrada."); return }
                } else {
                    reader?.acquireLatestImage()?.use { frame ->
                        val plane = frame.planes[0]
                        val padded = Bitmap.createBitmap(plane.rowStride / plane.pixelStride, height, Bitmap.Config.ARGB_8888)
                        try {
                            padded.copyPixelsFromBuffer(plane.buffer)
                            val cropped = Bitmap.createBitmap(padded, 0, 0, width, height)
                            try { cached = ByteArrayOutputStream().use { output -> cropped.compress(Bitmap.CompressFormat.JPEG, 55, output); output.toByteArray() } }
                            finally { if (cropped !== padded) cropped.recycle() }
                        } finally { padded.recycle() }
                    }
                    cached?.let { bytes ->
                        if (bytes.size > 2 * 1024 * 1024) { finish("Quadro acima do limite seguro."); return }
                        sentAt = SystemClock.elapsedRealtime(); waiting.set(true)
                        if (socket?.send(bytes.toByteString()) != true) { finish("Conexão encerrada."); return }
                    }
                }
            } catch (_: Exception) { finish("Captura interrompida. Autorize novamente para continuar."); return }
            capture.postDelayed(this, 120)
        }
    }
    private fun finish(message: String) {
        authorized = false
        if (!closed.compareAndSet(false, true)) return
        main.post { status = message; stopSelf() }
    }
    override fun onDestroy() {
        authorized = false; running = false; closed.set(true); physicalSize = null
        watcher?.close(); scope.cancel(); socket?.cancel()
        main.removeCallbacks(controlMonitor)
        capture.removeCallbacksAndMessages(null)
        // Release capture resources on the same handler, after any current frame.
        capture.post { display?.release(); reader?.close(); projection?.stop(); cached = null; thread.quitSafely() }
        unregisterReceiver(screenOff); stopForeground(STOP_FOREGROUND_REMOVE)
        super.onDestroy()
    }
}
