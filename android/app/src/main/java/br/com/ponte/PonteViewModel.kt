package br.com.ponte

import android.app.Application
import android.net.Uri
import android.os.Build
import android.provider.OpenableColumns
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive

class PonteViewModel(application: Application) : AndroidViewModel(application) {
    private val store = SecureConnectionStore(application)
    var connection by mutableStateOf<Connection?>(null); private set
    var files by mutableStateOf<List<RemoteFile>>(emptyList()); private set
    var busy by mutableStateOf(false); private set
    var message by mutableStateOf("Conecte seu computador para começar."); private set
    var pendingDownload: RemoteFile? = null
    private var operationTarget: Connection? = null
    private var filePolling: Job? = null
    private var fileSessionId = ""
    private var cachedConnection: Connection? = null
    private var cachedClient: PonteClient? = null
    private var lastDiscovery = 0L
    private var lastNetworkRead = 0L
    private val networkWatcher = NetworkWatcher(application) {
        viewModelScope.launch {
            if (filePolling?.isActive != true || busy) return@launch
            val current = connection ?: return@launch
            try {
                val found = ConnectionRouting.resolve(current, preferLocal = true)
                if (connection == current && !busy) { withContext(Dispatchers.IO) { store.save(found) }; connection = found; message = "${found.routeLabel} · conexão restabelecida." }
            } catch (e: CancellationException) { throw e }
            catch (_: Exception) { message = "Rede alterada. Aguardando o PC ficar acessível; arquivos não foram apagados." }
        }
    }
    private suspend fun rememberNetwork(current: Connection): Connection {
        val targets = try { client(current).wakeTargets() } catch (e: CancellationException) { throw e } catch (_: Exception) { return current }
        val internet = try { client(current).internetAddress() } catch (e: CancellationException) { throw e } catch (_: Exception) { null }
        val updated = current.copy(wakeTargets = targets.ifEmpty { current.wakeTargets },
            internetHost = if (current.learnInternet) internet ?: current.internetHost else current.internetHost)
        if (connection == current && updated != current) { withContext(Dispatchers.IO) { store.save(updated) }; connection = updated }
        return updated
    }
    private suspend fun recover(current: Connection): Connection? {
        val found = try { ConnectionRouting.resolve(current) } catch (e: CancellationException) { throw e }
            catch (e: PonteHttpException) { throw e }
            catch (_: Exception) { LanNetwork.recover(current) ?: return null }
        if (connection != current) return null
        withContext(Dispatchers.IO) { store.save(found) }
        connection = found
        return found
    }
    fun findPc() = operation("Procurando o PC pareado na rede local…") {
        val found = recover(requireNotNull(connection)) ?: error("PC não encontrado. Confira a rede/firewall ou informe o IP manualmente.")
        rememberNetwork(found)
        "${found.routeLabel} · PC localizado; identidade verificada."
    }
    fun wakePc() = operation("Enviando Wake-on-LAN…") {
        LanNetwork.wake(requireNotNull(connection).wakeTargets)
        "Pacote enviado. Acordar depende da placa de rede/BIOS e da rede local; aguarde alguns segundos."
    }
    private fun client(value: Connection = requireNotNull(connection)): PonteClient {
        if (cachedConnection != value) { cachedConnection = value; cachedClient = PonteClient(value) }
        return requireNotNull(cachedClient)
    }
    fun setHomeVisible(visible: Boolean) {
        if (!visible) { filePolling?.cancel(); filePolling = null; return }
        if (filePolling?.isActive == true) return
        filePolling = viewModelScope.launch {
            val saved = withContext(Dispatchers.IO) { runCatching { store.load() }.getOrNull() }
            if (saved != null && saved.fingerprint == connection?.fingerprint && saved.token == connection?.token) connection = saved
            connection?.let { current ->
                try {
                    val preferred = ConnectionRouting.resolve(current, preferLocal = true)
                    if (connection == current && preferred != current) { withContext(Dispatchers.IO) { store.save(preferred) }; connection = preferred }
                } catch (e: CancellationException) { throw e } catch (_: Exception) { }
            }
            var first = true
            while (isActive) {
                if (!first) delay(2500)
                first = false
                val current = connection ?: continue
                if (busy) continue
                try {
                    val api = client(current)
                    val status = api.pcStatus()
                    if (connection != current || busy) continue
                    if (fileSessionId != status.fileSessionId) { files = emptyList(); fileSessionId = status.fileSessionId }
                    val available = api.listFiles()
                    if (connection == current && !busy) {
                        files = available
                        if (System.nanoTime() - lastNetworkRead > 30_000_000_000L) { lastNetworkRead = System.nanoTime(); rememberNetwork(current) }
                    }
                } catch (e: CancellationException) { throw e }
                catch (e: Exception) {
                    val now = System.nanoTime()
                    if (e !is PonteHttpException && now - lastDiscovery > 30_000_000_000L) { lastDiscovery = now; recover(current) }
                }
            }
        }
    }
    init {
        try {
            connection = store.load()
            if (connection != null) refresh()
        } catch (_: Exception) { message = "Não foi possível recuperar a conexão. Faça um novo pareamento." }
    }
    fun showMessage(text: String) { message = text }
    private fun operation(text: String, action: suspend () -> String) {
        if (busy) return
        busy = true
        operationTarget = connection
        message = text
        viewModelScope.launch {
            try { message = action() }
            catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                message = ConnectionDiagnostics.describe(e, operationTarget?.host, operationTarget?.port ?: 47831)
            }
            finally { busy = false; operationTarget = null }
        }
    }
    fun pair(link: String) = operation("Solicitando acesso… Aprove o pedido no Windows.") {
        val (candidate, code) = Connection.parsePairing(link)
        operationTarget = candidate
        val paired = PonteClient(candidate).pair(code, "${Build.MANUFACTURER} ${Build.MODEL}".take(80))
        withContext(Dispatchers.IO) { store.save(paired) }
        connection = paired
        rememberNetwork(paired)
        fileSessionId = client(paired).pcStatus().fileSessionId
        files = client(paired).listFiles()
        "Pareado com ${paired.pcName}. Conexão segura pronta."
    }
    fun refresh() = operation("Conectando ao PC…") {
        var current = requireNotNull(connection)
        val status = try { client(current).pcStatus() } catch (e: Exception) {
            if (e is CancellationException || e is PonteHttpException) throw e
            current = recover(current) ?: throw e
            client(current).pcStatus()
        }
        val api = client(current)
        if (fileSessionId != status.fileSessionId) files = emptyList()
        fileSessionId = status.fileSessionId
        files = api.listFiles()
        rememberNetwork(current)
        "${current.routeLabel} · ${files.size} arquivo(s) disponível(is)."
    }
    fun changeHost(host: String) = operation("Atualizando endereço…") {
        require(Connection.validHost(host.trim())) { "Informe o IPv4 da rede local." }
        val updated = requireNotNull(connection).copy(host = host.trim(), localHost = host.trim())
        operationTarget = updated
        val available = client(updated).listFiles()
        withContext(Dispatchers.IO) { store.save(updated) }
        connection = updated
        files = available
        "Endereço atualizado. A identidade do PC foi verificada."
    }
    fun configureInternet(host: String) = operation("Verificando acesso direto à internet…") {
        val current = requireNotNull(connection)
        if (host.isBlank()) {
            val updated = current.copy(internetHost = "", host = current.localHost, learnInternet = false)
            withContext(Dispatchers.IO) { store.save(updated) }; connection = updated
            "Endereço de internet removido. A conexão local foi preservada."
        } else {
            val address = NetworkHost.internet(host)
            val candidate = current.copy(host = address, internetHost = address, learnInternet = false)
            operationTarget = candidate
            client(candidate).pcStatus() // exact pin + bearer checked before persistence
            withContext(Dispatchers.IO) { store.save(candidate) }; connection = candidate
            "Internet direta configurada com identidade verificada. Teste agora pelo 4G/5G."
        }
    }
    fun learnInternet() = operation("Aprendendo IPv6 do PC autenticado…") {
        val current = requireNotNull(connection).copy(learnInternet = true)
        connection = current
        val updated = rememberNetwork(current)
        withContext(Dispatchers.IO) { store.save(updated) }
        if (updated.internetHost.isEmpty()) "Nenhum IPv6 disponível. Ative a permissão de internet no Windows e confira a rede."
        else "IPv6 aprendido. Isso não confirma entrada pelo roteador; teste pelo 4G/5G."
    }
    fun forget() = operation("Removendo conexão local…") {
        withContext(Dispatchers.IO) { store.clear() }
        connection = null
        files = emptyList()
        "Conexão removida do telefone. Para revogar o acesso, remova-o também no PC."
    }
    fun upload(uri: Uri) = operation("Enviando arquivo… Mantenha o aplicativo aberto.") {
        val resolver = getApplication<Application>().contentResolver
        val (name, size) = withContext(Dispatchers.IO) {
            resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { cursor ->
                if (cursor.moveToFirst()) {
                    val nameIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                    val sizeIndex = cursor.getColumnIndex(OpenableColumns.SIZE)
                    (if (nameIndex >= 0) cursor.getString(nameIndex) ?: "arquivo" else "arquivo") to
                        (if (sizeIndex >= 0 && !cursor.isNull(sizeIndex)) cursor.getLong(sizeIndex) else -1L)
                } else "arquivo" to -1L
            } ?: ("arquivo" to -1L)
        }
        val api = client()
        api.upload(name, size) { resolver.openInputStream(uri) ?: error("Não foi possível abrir o arquivo.") }
        files = api.listFiles()
        "Arquivo enviado ao PC: $name"
    }
    fun download(file: RemoteFile, uri: Uri) = operation("Baixando ${file.name}… Mantenha o aplicativo aberto.") {
        val resolver = getApplication<Application>().contentResolver
        client().download(file) {
            resolver.openOutputStream(uri, "wt") ?: error("Não foi possível salvar o arquivo.")
        }
        "Arquivo salvo: ${file.name}"
    }
    override fun onCleared() { networkWatcher.close(); super.onCleared() }
}
