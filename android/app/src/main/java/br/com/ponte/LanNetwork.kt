package br.com.ponte

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException
import java.util.UUID

data class WakeTarget(val mac: String, val broadcast: String)

object LanNetwork {
    fun magicPacket(mac: String): ByteArray {
        require(mac.matches(Regex("[0-9a-fA-F]{12}"))) { "MAC inválido." }
        val address = mac.chunked(2).map { it.toInt(16).toByte() }.toByteArray()
        return ByteArray(102) { index -> if (index < 6) 0xff.toByte() else address[(index - 6) % 6] }
    }
    suspend fun wake(targets: List<WakeTarget>) = withContext(Dispatchers.IO) {
        require(targets.isNotEmpty()) { "Conecte ao PC ligado uma vez para aprender os dados de Wake-on-LAN." }
        DatagramSocket().use { socket ->
            socket.broadcast = true
            repeat(3) {
                for (target in targets) {
                    require(Connection.validHost(target.broadcast))
                    val bytes = magicPacket(target.mac)
                    socket.send(DatagramPacket(bytes, bytes.size, InetAddress.getByName(target.broadcast), 9))
                }
            }
        }
    }
    suspend fun recover(connection: Connection): Connection? = try { discover(connection) }
        catch (e: kotlinx.coroutines.CancellationException) { throw e }
        catch (_: Exception) { null }
    private suspend fun discover(connection: Connection): Connection? = withContext(Dispatchers.IO) {
        val nonce = UUID.randomUUID().toString().replace("-", "")
        val bytes = "PONTE_DISCOVER $nonce".toByteArray(Charsets.US_ASCII)
        DatagramSocket().use { socket ->
            socket.broadcast = true
            socket.soTimeout = 400
            val broadcasts = mutableSetOf("255.255.255.255")
            NetworkInterface.getNetworkInterfaces().toList().filter { it.isUp && !it.isLoopback }.forEach { adapter ->
                adapter.interfaceAddresses.forEach { it.broadcast?.hostAddress?.let(broadcasts::add) }
            }
            for (host in broadcasts) runCatching { socket.send(DatagramPacket(bytes, bytes.size, InetAddress.getByName(host), 47832)) }
            val deadline = System.nanoTime() + 2_000_000_000L
            val seen = mutableSetOf<String>()
            while (System.nanoTime() < deadline) {
                currentCoroutineContext().ensureActive()
                val packet = DatagramPacket(ByteArray(1024), 1024)
                try { socket.receive(packet) } catch (_: SocketTimeoutException) { continue }
                val json = runCatching { JSONObject(String(packet.data, 0, packet.length, Charsets.UTF_8)) }.getOrNull() ?: continue
                val host = packet.address.hostAddress ?: continue
                if (!Connection.validHost(host) || !seen.add(host) || json.optString("nonce") != nonce || !json.optString("fingerprint").equals(connection.fingerprint, true) || json.optInt("port") != connection.port) continue
                val candidate = connection.copy(host = host, localHost = host)
                // Discovery is untrusted. TLS pin is checked before any bearer is sent.
                try { PonteClient(candidate).pcStatus(); return@withContext candidate }
                catch (e: kotlinx.coroutines.CancellationException) { throw e }
                catch (_: Exception) { }
            }
        }
        null
    }
}
