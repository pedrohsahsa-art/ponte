package br.com.ponte

import android.os.Bundle
import android.os.Build
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.media.projection.MediaProjectionConfig
import android.media.projection.MediaProjectionManager
import android.provider.Settings
import androidx.core.content.ContextCompat
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.background
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.ui.Alignment
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import java.util.Locale

class MainActivity : ComponentActivity() {
    private fun screenPermissionIntent(): Intent {
        val manager = getSystemService(MediaProjectionManager::class.java)
        return if (Build.VERSION.SDK_INT >= 34) manager.createScreenCaptureIntent(MediaProjectionConfig.createConfigForDefaultDisplay()) else manager.createScreenCaptureIntent()
    }
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            PonteTheme {
                Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
                    PonteScreen(viewModel())
                }
            }
        }
    }
    private fun scanPairing(vm: PonteViewModel) {
        val options = GmsBarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).enableAutoZoom().build()
        GmsBarcodeScanning.getClient(this, options).startScan()
            .addOnSuccessListener { barcode -> barcode.rawValue?.let(vm::pair) }
            .addOnFailureListener { vm.showMessage("Leitor indisponível. Use o link de pareamento. ${it.localizedMessage ?: ""}") }
    }
    @Composable
    private fun PonteScreen(vm: PonteViewModel) {
        var showRemote by rememberSaveable { mutableStateOf(false) }
        val remoteVm: RemoteViewModel = viewModel()
        if (showRemote && vm.connection != null) {
            RemoteScreen(this, vm.connection!!, remoteVm) { showRemote = false }
            return
        }
        val owner = LocalLifecycleOwner.current
        DisposableEffect(owner) {
            val observer = LifecycleEventObserver { _, event ->
                if (event == Lifecycle.Event.ON_START) vm.setHomeVisible(true)
                if (event == Lifecycle.Event.ON_STOP) vm.setHomeVisible(false)
            }
            owner.lifecycle.addObserver(observer)
            vm.setHomeVisible(owner.lifecycle.currentState.isAtLeast(Lifecycle.State.STARTED))
            onDispose { owner.lifecycle.removeObserver(observer); vm.setHomeVisible(false) }
        }
        var pairingText by remember { mutableStateOf("") }
        var connectionSettings by rememberSaveable { mutableStateOf(false) }
        var section by rememberSaveable { mutableStateOf("devices") }
        var sharingDisclosure by remember { mutableStateOf(false) }
        val projection = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
            if (result.resultCode == Activity.RESULT_OK && result.data != null) {
                ContextCompat.startForegroundService(this, Intent(this, AndroidShareService::class.java).putExtra("result", result.resultCode).putExtra("data", result.data))
            } else vm.showMessage("Captura não autorizada. Nenhuma tela foi compartilhada.")
        }
        val notifications = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
            if (granted) projection.launch(screenPermissionIntent()) else vm.showMessage("Permita notificações para manter o botão Parar visível durante o compartilhamento.")
        }
        if (sharingDisclosure) AlertDialog(
            onDismissRequest = { sharingDisclosure = false },
            title = { Text("Compartilhar Android com o PC?") },
            text = { Text("O PC pareado poderá ver a tela inteira. Se você ativar a Acessibilidade do Ponte, poderá também clicar, deslizar e substituir o texto de campos focados. Senhas, configurações e permissões não são controladas.\n\nAbra primeiro Abrir Android no Windows. Depois de autorizar, saia desta tela manualmente para usar outro app. Você pode parar pela notificação. Bloqueio, rotação e perda de conexão encerram a sessão.") },
            confirmButton = { TextButton(onClick = {
                sharingDisclosure = false
                if (Build.VERSION.SDK_INT >= 33 && ContextCompat.checkSelfPermission(this, android.Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) notifications.launch(android.Manifest.permission.POST_NOTIFICATIONS)
                else projection.launch(screenPermissionIntent())
            }) { Text("Continuar e autorizar") } },
            dismissButton = { TextButton(onClick = { sharingDisclosure = false }) { Text("Cancelar") } }
        )
        var repairPairing by remember { mutableStateOf(false) }
        LaunchedEffect(vm.connection) { repairPairing = false; pairingText = "" }
        var hostText by remember(vm.connection?.localHost) { mutableStateOf(vm.connection?.localHost ?: "") }
        var internetText by remember(vm.connection?.internetHost) { mutableStateOf(vm.connection?.internetHost ?: "") }
        val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
            if (uri != null) vm.upload(uri)
        }
        val saver = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/octet-stream")) { uri ->
            val file = vm.pendingDownload
            vm.pendingDownload = null
            if (uri != null && file != null) vm.download(file, uri)
        }
        LazyColumn(modifier = Modifier.fillMaxSize().safeDrawingPadding().padding(horizontal = 12.dp), verticalArrangement = Arrangement.spacedBy(12.dp), contentPadding = PaddingValues(bottom = 24.dp)) {
            item {
                XpCaption("Ponte · Meus dispositivos")
            }
            item {
                Card(colors = CardDefaults.cardColors(containerColor = Color.White), modifier = Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(16.dp)) {
                        Text(if (vm.connection == null) "PRIMEIRA CONEXÃO" else "COMPUTADOR PAREADO", style = MaterialTheme.typography.labelSmall, color = PonteTeal, letterSpacing = 1.sp)
                        Spacer(Modifier.height(6.dp))
                        Text(if (vm.connection == null) "Vamos conectar?" else vm.connection!!.pcName, fontWeight = FontWeight.SemiBold)
                        Spacer(Modifier.height(6.dp))
                        Text(vm.message, style = MaterialTheme.typography.bodyMedium, color = Color(0xFF536C78))
                        if (vm.busy) { Spacer(Modifier.height(12.dp)); LinearProgressIndicator(Modifier.fillMaxWidth()) }
                    }
                }
            }
            if (vm.connection == null) {
                item {
                    Text("1. Abra o Ponte no Windows.\n2. Gere o QR Code.\n3. Escaneie e aprove no computador.")
                    Spacer(Modifier.height(12.dp))
                    Button(onClick = { scanPairing(vm) }, enabled = !vm.busy, modifier = Modifier.fillMaxWidth()) { Text("Escanear QR Code") }
                }
                item {
                    Text("Alternativa sem câmera", fontWeight = FontWeight.SemiBold)
                    OutlinedTextField(value = pairingText, onValueChange = { pairingText = it }, label = { Text("Cole o link ponte://pair…") }, modifier = Modifier.fillMaxWidth(), enabled = !vm.busy)
                    Button(onClick = { vm.pair(pairingText); pairingText = "" }, enabled = !vm.busy && pairingText.isNotBlank()) { Text("Conectar pelo link") }
                }
            } else {
                item {
                    Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        listOf("devices" to "Controlar", "files" to "Arquivos", "settings" to "Conexão").forEach { (key, label) ->
                            FilterChip(selected = section == key, onClick = { section = key }, label = { Text(label) }, shape = RoundedCornerShape(3.dp))
                        }
                    }
                }
                if (section == "devices") item {
                    Surface(shape = RoundedCornerShape(4.dp), color = Color.White, modifier = Modifier.fillMaxWidth()) {
                        Column(Modifier.padding(20.dp)) {
                            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                Column(Modifier.weight(1f)) {
                                     Text("Usar meu computador", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold, color = PonteTeal)
                                    Text("Tela, teclado e áudio.", style = MaterialTheme.typography.bodySmall, color = Color(0xFF607782), modifier = Modifier.padding(top = 6.dp))
                                }
                                DeviceGlyph(Modifier.size(44.dp))
                            }
                            Button(onClick = { showRemote = true }, enabled = !vm.busy, modifier = Modifier.fillMaxWidth().padding(top = 16.dp).heightIn(min = 48.dp)) { Text("Controlar PC", fontWeight = FontWeight.SemiBold) }
                            Text("Autorização de controle gerenciada no Windows.", color = Color(0xFF607782), style = MaterialTheme.typography.labelSmall, modifier = Modifier.padding(top = 10.dp))
                        }
                    }
                }
                if (section == "devices") item {
                    XpPanel("Mostrar meu Android no PC", R.drawable.ponte_xp_phone) {
                        Text(AndroidShareService.status, style = MaterialTheme.typography.bodySmall)
                        Text(if (PonteAccessibilityService.connected) "Acessibilidade conectada · o PC mostrará o resultado de cada ação." else "Somente visualização · ative a Acessibilidade em Permitir controle.", style = MaterialTheme.typography.bodySmall, color = if (PonteAccessibilityService.connected) PonteTeal else Color(0xFF95641B))
                        if (AndroidShareService.running) Button(onClick = { startService(Intent(this@MainActivity, AndroidShareService::class.java).setAction(AndroidShareService.STOP)) }) { Text("Parar compartilhamento") }
                        else OutlinedButton(onClick = { sharingDisclosure = true }, enabled = !vm.busy) { Text("Compartilhar Android") }
                        TextButton(onClick = { startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS)) }) { Text("Permitir controle") }
                        Text("Acessibilidade é opcional para assistir e obrigatória para controlar. Ative somente o serviço Ponte nas configurações do Android.", style = MaterialTheme.typography.bodySmall, color = Color(0xFF607782))
                    }
                }
                if (section == "settings") item {
                    Card(colors = CardDefaults.cardColors(containerColor = Color.White), modifier = Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text("Sua conexão", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                            Text("${vm.connection!!.routeLabel} · TLS", style = MaterialTheme.typography.bodySmall, color = Color(0xFF607782))
                        }
                        TextButton(onClick = { connectionSettings = !connectionSettings }) { Text(if (connectionSettings) "Recolher" else "Avançado") }
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        TextButton(onClick = vm::findPc, enabled = !vm.busy) { Text("Localizar PC") }
                        TextButton(onClick = vm::wakePc, enabled = !vm.busy && vm.connection!!.wakeTargets.isNotEmpty()) { Text("Acordar PC") }
                    }
                    if (connectionSettings) {
                    OutlinedTextField(value = hostText, onValueChange = { hostText = it }, label = { Text("IPv4 do PC na rede local") }, singleLine = true, enabled = !vm.busy, modifier = Modifier.fillMaxWidth())
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        OutlinedButton(onClick = { vm.changeHost(hostText) }, enabled = !vm.busy) { Text("Trocar endereço") }
                        TextButton(onClick = vm::forget, enabled = !vm.busy) { Text("Esquecer PC") }
                    }
                    HorizontalDivider(Modifier.padding(vertical = 8.dp))
                    Text("Internet direta · sem mensalidade", style = MaterialTheme.typography.titleSmall)
                    OutlinedTextField(value = internetText, onValueChange = { internetText = it }, label = { Text("IPv6 público do PC ou domínio DDNS") }, singleLine = true, enabled = !vm.busy, modifier = Modifier.fillMaxWidth())
                    OutlinedButton(onClick = { vm.configureInternet(internetText) }, enabled = !vm.busy) { Text(if (internetText.isBlank()) "Remover internet" else "Verificar e salvar") }
                    TextButton(onClick = vm::learnInternet, enabled = !vm.busy) { Text("Aprender IPv6 do PC") }
                    Text("Ative a permissão de internet no Windows e atualize aqui ainda na rede local para aprender o IPv6. Depois teste sem Wi-Fi. O roteador pode exigir uma regra IPv6 TCP 47831 somente para este PC; não use DMZ. DDNS é opcional e precisa ser configurado separadamente.", style = MaterialTheme.typography.bodySmall)
                    OutlinedButton(onClick = { repairPairing = !repairPairing }, enabled = !vm.busy) { Text("Parear novamente") }
                    Text("Wake-on-LAN requer suporte e configuração da placa/BIOS. Não altera configurações do Windows.", style = MaterialTheme.typography.bodySmall)
                    if (repairPairing) {
                        Text("Gere um novo QR no Windows. A conexão anterior só é substituída depois da aprovação; seus arquivos não são apagados.", style = MaterialTheme.typography.bodySmall)
                        Button(onClick = { scanPairing(vm) }, enabled = !vm.busy) { Text("Escanear QR Code") }
                        OutlinedTextField(pairingText, onValueChange = { pairingText = it }, label = { Text("Ou cole o novo link ponte://pair…") }, modifier = Modifier.fillMaxWidth(), enabled = !vm.busy)
                        Button(onClick = { vm.pair(pairingText); pairingText = "" }, enabled = !vm.busy && pairingText.isNotBlank()) { Text("Parear pelo link") }
                    }
                    }
                    }
                    }
                }
                if (section == "files") item {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Button(onClick = { picker.launch(arrayOf("*/*")) }, enabled = !vm.busy) { Text("Enviar ao PC") }
                        OutlinedButton(onClick = vm::refresh, enabled = !vm.busy) { Text("Atualizar") }
                    }
                    Spacer(Modifier.height(14.dp))
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                        Text("Seus arquivos", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
                        Surface(color = MaterialTheme.colorScheme.primaryContainer, shape = RoundedCornerShape(12.dp)) { Text("${vm.files.size}", modifier = Modifier.padding(horizontal = 12.dp, vertical = 5.dp), style = MaterialTheme.typography.labelMedium) }
                    }
                    Text("Temporários por sessão · sem teto de tamanho", color = Color(0xFF607782), fontSize = 12.sp, modifier = Modifier.padding(top = 4.dp))
                    Text("Salve o que quiser manter antes de reabrir o Ponte no PC.", color = Color(0xFF95641B), fontSize = 12.sp)
                }
                if (section == "files" && vm.files.isEmpty()) item {
                    Surface(shape = RoundedCornerShape(4.dp), color = Color(0xFFD6DFF7), modifier = Modifier.fillMaxWidth()) {
                        Column(Modifier.padding(26.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                            DeviceGlyph(document = true)
                            Text("Espaço para compartilhar", style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(top = 10.dp))
                            Text("Envie um arquivo pelo telefone ou adicione no PC. Ele aparece aqui.", style = MaterialTheme.typography.bodyMedium, color = Color(0xFF607782), modifier = Modifier.padding(top = 6.dp))
                        }
                    }
                }
                if (section == "files") items(vm.files, key = { it.id }) { file ->
                    Card(colors = CardDefaults.cardColors(containerColor = Color.White), modifier = Modifier.fillMaxWidth()) {
                        Row(Modifier.padding(14.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                            Surface(color = MaterialTheme.colorScheme.surfaceVariant, shape = RoundedCornerShape(12.dp)) { DeviceGlyph(Modifier.padding(6.dp).size(32.dp), document = true) }
                            Column(Modifier.weight(1f)) {
                                Text(file.name, fontWeight = FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis)
                                Text("${formatSize(file.size)} · ${file.source}", color = Color(0xFF64748B), fontSize = 12.sp)
                            }
                            TextButton(onClick = { vm.pendingDownload = file; saver.launch(file.name) }, enabled = !vm.busy) { Text("Baixar ↓") }
                        }
                    }
                }
            }
            item {
                Text("Ponte 0.8.0 · Clássico XP · conexão segura", fontSize = 11.sp, color = Color(0xFF526174), modifier = Modifier.padding(vertical = 12.dp), lineHeight = 18.sp)
            }
        }
    }
    private fun formatSize(size: Long): String = when {
        size >= 1024L * 1024 * 1024 -> String.format(Locale.getDefault(), "%.1f GiB", size / (1024.0 * 1024 * 1024))
        size >= 1024 * 1024 -> String.format(Locale.getDefault(), "%.1f MiB", size / (1024.0 * 1024))
        size >= 1024 -> String.format(Locale.getDefault(), "%.1f KiB", size / 1024.0)
        else -> "$size bytes"
    }
}
