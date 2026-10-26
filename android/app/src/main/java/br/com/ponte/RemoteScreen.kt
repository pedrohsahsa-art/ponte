package br.com.ponte

import android.content.pm.ActivityInfo
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import kotlinx.coroutines.delay
import kotlin.math.roundToInt

@Composable
fun RemoteScreen(activity: ComponentActivity, connection: Connection, vm: RemoteViewModel, onBack: () -> Unit) {
    var keyboardBridge by remember { mutableStateOf<LiveRemoteKeyboard?>(null) }
    var tools by remember { mutableStateOf<String?>(null) }
    var chrome by remember { mutableStateOf(true) }
    var viewSize by remember { mutableStateOf(IntSize.Zero) }
    var transform by remember { mutableStateOf(ViewportTransform()) }
    val owner = LocalLifecycleOwner.current
    val view = LocalView.current
    val keyboardVisible = WindowInsets.ime.getBottom(LocalDensity.current) > 0
    BackHandler {
        if (keyboardVisible) { vm.dismissKeyboard(); keyboardBridge?.dismiss(); tools = null }
        else { vm.leave(); onBack() }
    }
    LaunchedEffect(connection) { vm.enter(connection) }
    LaunchedEffect(vm.monitorId) { transform = ViewportTransform() }
    LaunchedEffect(viewSize, vm.frameWidth, vm.frameHeight) {
        transform = ScreenMapping.clamp(viewSize.width, viewSize.height, vm.frameWidth, vm.frameHeight, transform)
    }
    LaunchedEffect(chrome, tools, vm.connected) {
        if (chrome && tools == null && vm.connected) { delay(6000); chrome = false }
    }
    DisposableEffect(owner, activity) {
        val previousOrientation = activity.requestedOrientation
        activity.requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
        activity.window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        val bars = WindowCompat.getInsetsController(activity.window, view)
        bars.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        bars.hide(WindowInsetsCompat.Type.systemBars())
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_STOP) vm.pause()
            if (event == Lifecycle.Event.ON_START) vm.resume()
        }
        owner.lifecycle.addObserver(observer)
        onDispose {
            owner.lifecycle.removeObserver(observer)
            keyboardBridge?.dismiss()
            vm.pause() // preserve desired connection during rotation/recreation
            activity.window.clearFlags(WindowManager.LayoutParams.FLAG_SECURE)
            bars.show(WindowInsetsCompat.Type.systemBars())
            activity.requestedOrientation = previousOrientation
        }
    }
    DisposableEffect(view, vm.connected) {
        view.keepScreenOn = vm.connected
        onDispose { view.keepScreenOn = false }
    }
    MaterialTheme(colorScheme = lightColorScheme(primary = Color(0xFF003399), primaryContainer = Color(0xFFDCEAFF), secondaryContainer = Color(0xFFD6DFF7), surface = Color(0xFFECE9D8))) {
        Box(Modifier.fillMaxSize().background(Color.Black)) {
            DesktopViewport(vm, transform, { transform = it }, { viewSize = it })
            AndroidView(
                factory = { context -> LiveRemoteKeyboard(context).also { bridge ->
                    bridge.onEdit = vm::edit
                    bridge.onRemoteKey = { vm.key(it) }
                    keyboardBridge = bridge
                } },
                modifier = Modifier.size(1.dp).align(Alignment.BottomStart),
                onRelease = { it.dismiss(); if (keyboardBridge === it) keyboardBridge = null },
                update = { it.updateRequests(vm.keyboardOpenRequest, vm.keyboardHideRequest, vm.keyboardResetRequest, vm.keyboardPassword) }
            )
            if (!vm.connected) {
                Surface(Modifier.align(Alignment.Center).widthIn(max = 520.dp).padding(24.dp), color = Color(0xF212242F), shape = RoundedCornerShape(24.dp)) {
                    Column(Modifier.padding(24.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        Text(if (vm.connecting) "PREPARANDO SUA TELA" else "CONTROLE REMOTO", style = MaterialTheme.typography.labelSmall, color = Color(0xFF87DED0))
                        Text(connection.pcName, style = MaterialTheme.typography.headlineSmall)
                        Text("Tentativa ${vm.attempts} · conexão com identidade verificada", style = MaterialTheme.typography.labelMedium)
                        Text(vm.phase, style = MaterialTheme.typography.labelMedium)
                        Text(vm.message)
                        if (vm.connecting) LinearProgressIndicator(Modifier.fillMaxWidth())
                        else Button(onClick = { vm.start(connection) }) { Text("Conectar") }
                        TextButton(onClick = { vm.leave(); onBack() }) { Text("Voltar ao início") }
                    }
                }
            }
            if (chrome) {
                Column(Modifier.align(Alignment.TopCenter).fillMaxWidth().safeDrawingPadding().padding(horizontal = 10.dp, vertical = 6.dp).clip(RoundedCornerShape(4.dp)).background(Color(0xFFECE9D8)).border(1.dp, Color(0xFF7F9DB9), RoundedCornerShape(4.dp)).padding(horizontal = 8.dp)) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        TextButton(onClick = { vm.leave(); onBack() }) { Text("← Voltar") }
                        Column(Modifier.weight(1f).padding(horizontal = 8.dp)) {
                            Text(connection.pcName, style = MaterialTheme.typography.titleSmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Text(if (vm.connected) "${vm.connectionRoute} · ${vm.fps} fps · ${(transform.zoom * 100).roundToInt()}%" else vm.phase, style = MaterialTheme.typography.labelSmall, color = Color(0xFF003399), maxLines = 1)
                        }
                        TextButton(onClick = { chrome = false; tools = null }) { Text("Recolher ↑") }
                    }
                    Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                        RemoteTool("Enquadrar", tools == "view", vm.connected) { tools = if (tools == "view") null else "view" }
                        RemoteTool("Teclado", tools == "keyboard", vm.connected) {
                            if (tools == "keyboard") { tools = null; vm.dismissKeyboard() }
                            else { tools = "keyboard"; vm.requestKeyboard() }
                        }
                        RemoteTool("Mouse/volume", tools == "mouse", vm.connected) { tools = if (tools == "mouse") null else "mouse"; vm.dismissKeyboard() }
                        RemoteTool("Arrastar", vm.dragMode, vm.connected, vm::toggleDragMode)
                        RemoteTool("Monitor/qualidade", tools == "display", vm.connected) { tools = if (tools == "display") null else "display" }
                        RemoteTool("Ouvir PC", vm.audioEnabled, vm.connected, vm::toggleAudio)
                        TextButton(onClick = { vm.stop(); tools = null }, enabled = vm.connected || vm.connecting) { Text("Desconectar") }
                    }
                    Text(if (vm.dragMode) "ARRASTAR ATIVO · deslize com um dedo e solte para largar" else vm.message, modifier = Modifier.padding(horizontal = 8.dp, vertical = 5.dp), style = MaterialTheme.typography.labelSmall, color = Color(0xFF526174), maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            } else {
                FilledTonalButton(onClick = { chrome = true }, modifier = Modifier.align(Alignment.TopEnd).safeDrawingPadding().padding(8.dp), contentPadding = PaddingValues(horizontal = 16.dp, vertical = 10.dp)) { Text("☰  Controles · ${vm.fps} fps") }
            }
            if (tools != null && vm.connected) {
                Column(Modifier.align(Alignment.BottomCenter).fillMaxWidth().imePadding().safeDrawingPadding().padding(10.dp).clip(RoundedCornerShape(4.dp)).background(Color(0xFFECE9D8)).padding(12.dp)) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(when (tools) { "keyboard" -> "Seu teclado, no PC"; "display" -> "Imagem do seu jeito"; "view" -> "Ajuste o enquadramento"; else -> "Mouse e volume do PC" }, style = MaterialTheme.typography.titleSmall, modifier = Modifier.weight(1f))
                        TextButton(onClick = { if (tools == "keyboard") vm.dismissKeyboard(); tools = null }) { Text("Fechar ↓") }
                    }
                    if (tools == "view") {
                        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            OutlinedButton(onClick = { transform = ViewportTransform() }) { Text("Ajustar") }
                            OutlinedButton(onClick = { transform = ScreenMapping.fill(viewSize.width, viewSize.height, vm.frameWidth, vm.frameHeight) }) { Text("Preencher") }
                            OutlinedButton(onClick = { transform = ScreenMapping.zoomAround(viewSize.width, viewSize.height, vm.frameWidth, vm.frameHeight, transform, 0.8f, viewSize.width / 2f, viewSize.height / 2f) }) { Text("Zoom −") }
                            OutlinedButton(onClick = { transform = ScreenMapping.zoomAround(viewSize.width, viewSize.height, vm.frameWidth, vm.frameHeight, transform, 1.25f, viewSize.width / 2f, viewSize.height / 2f) }) { Text("Zoom +") }
                            OutlinedButton(onClick = { activity.requestedOrientation = if (viewSize.width > viewSize.height) ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT else ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE }) { Text("Girar tela") }
                        }
                    } else if (tools == "keyboard") {
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                            Text("Digitação em tempo real no campo do PC · sem botão Enviar", modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodySmall)
                            TextButton(onClick = { tools = null; vm.dismissKeyboard() }) { Text("Ocultar teclado") }
                        }
                        Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            listOf("Enter" to "enter", "⌫" to "backspace", "Tab" to "tab", "Esc" to "escape", "←" to "left", "↑" to "up", "↓" to "down", "→" to "right", "Alt+Tab" to "alttab", "Ctrl+A" to "selectall", "Ctrl+C" to "copy", "Ctrl+V" to "paste", "Ctrl+Z" to "undo", "Del" to "delete").forEach { (label, key) ->
                                OutlinedButton(onClick = { vm.key(key) }, contentPadding = PaddingValues(horizontal = 12.dp)) { Text(label) }
                            }
                        }
                    } else if (tools == "display") {
                        Row(Modifier.horizontalScroll(rememberScrollState())) {
                            vm.monitors.forEach { monitor ->
                                TextButton(onClick = { vm.selectMonitor(monitor.id) }) { Text("${if (vm.monitorId == monitor.id || vm.monitorId.isEmpty() && monitor.primary) "✓ " else ""}${monitor.name} ${monitor.width}×${monitor.height}") }
                            }
                        }
                        Row(Modifier.horizontalScroll(rememberScrollState())) {
                            listOf("auto" to "Automática", "fluid" to "Fluidez", "balanced" to "Equilibrada", "sharp" to "Nitidez").forEach { (value, label) ->
                                TextButton(onClick = { vm.chooseQuality(value) }) { Text("${if (value == "auto" && vm.automaticQuality || value == vm.profile && !vm.automaticQuality) "✓ " else ""}$label") }
                            }
                        }
                        Text("Perfil atual: ${vm.profile}. Arrastar: ative o modo e deslize com um dedo; solte para largar.", style = MaterialTheme.typography.bodySmall)
                    } else Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        OutlinedButton(onClick = { vm.scroll(120) }) { Text("Rolar ↑") }
                        OutlinedButton(onClick = { vm.scroll(-120) }) { Text("Rolar ↓") }
                        OutlinedButton(onClick = { vm.key("volumedown") }) { Text("Volume −") }
                        OutlinedButton(onClick = { vm.key("volumeup") }) { Text("Volume +") }
                        OutlinedButton(onClick = { vm.key("mute") }) { Text("Mudo") }
                        TextButton(onClick = { tools = null }) { Text("Fechar") }
                    }
                }
            }
        }
    }
}

@Composable
private fun RemoteTool(label: String, selected: Boolean, enabled: Boolean, onClick: () -> Unit) {
    FilterChip(selected = selected, onClick = onClick, enabled = enabled, label = { Text(label, style = MaterialTheme.typography.labelMedium) }, shape = RoundedCornerShape(12.dp))
}

// Only this subtree reads the bitmap; avoid recomposing every toolbar per frame.
@Composable
private fun DesktopViewport(vm: RemoteViewModel, transform: ViewportTransform, onTransform: (ViewportTransform) -> Unit, onSize: (IntSize) -> Unit) {
    val currentTransform by rememberUpdatedState(transform)
    val changeTransform by rememberUpdatedState(onTransform)
    val bitmap = vm.frame
    val width = vm.frameWidth
    val height = vm.frameHeight
    Box(Modifier.fillMaxSize().clipToBounds().onSizeChanged(onSize)
        .pointerInput(width, height, vm.connected, vm.dragMode, vm.monitorReady) {
            fun point(x: Float, y: Float) = ScreenMapping.point(size.width, size.height, width, height, x, y, currentTransform)
            if (vm.dragMode) {
                try {
                    detectDragGestures(
                        onDragStart = { p -> point(p.x, p.y)?.let(vm::beginDrag) },
                        onDragEnd = vm::endDrag,
                        onDragCancel = vm::endDrag,
                        onDrag = { change, _ -> point(change.position.x, change.position.y)?.let(vm::move); change.consume() }
                    )
                } finally { vm.endDrag() }
            } else detectTapGestures(
                onTap = { p -> point(p.x, p.y)?.let { vm.click(it) } },
                onDoubleTap = { p -> point(p.x, p.y)?.let { vm.click(it, "doubleclick") } },
                onLongPress = { p -> point(p.x, p.y)?.let { vm.click(it, "rightclick") } }
            )
        }
        .pointerInput(width, height, vm.connected, vm.dragMode) {
            if (!vm.dragMode) detectTransformGestures { center, pan, zoom, _ ->
                if (zoom != 1f || currentTransform.zoom > 1.01f) {
                    changeTransform(ScreenMapping.zoomAround(size.width, size.height, width, height, currentTransform, zoom, center.x, center.y, pan.x, pan.y))
                } else ScreenMapping.point(size.width, size.height, width, height, center.x, center.y, currentTransform)?.let(vm::move)
            }
        }) {
        if (bitmap != null) Image(bitmap.asImageBitmap(), contentDescription = "Monitor selecionado do PC", contentScale = ContentScale.Fit,
            modifier = Modifier.fillMaxSize().graphicsLayer {
                scaleX = transform.zoom; scaleY = transform.zoom
                translationX = transform.panX; translationY = transform.panY
            })
    }
}
