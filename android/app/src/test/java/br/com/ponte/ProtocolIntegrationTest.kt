package br.com.ponte

import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.flow.take
import kotlinx.coroutines.flow.collect
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.IOException
import javax.net.ssl.SSLException

class ProtocolIntegrationTest {
    @Test fun realKotlinClientAgainstWindowsHttpsServer() = runBlocking {
        val configPath = System.getenv("PONTE_TEST_CONFIG")
        assumeTrue("Run scripts/test-android-protocol.ps1 for real HTTPS integration", !configPath.isNullOrEmpty())
        val config = JSONObject(File(requireNotNull(configPath)).readText())
        val expectedJpeg = java.util.Base64.getDecoder().decode(config.getString("frameBase64"))
        val connection = Connection(config.getString("host"), config.getInt("port"), config.getString("fingerprint"))
        val code = config.getString("code")
        var wrongPinRejected = false
        try { PonteClient(connection.copy(fingerprint = "00".repeat(32))).listFiles() }
        catch (_: SSLException) { wrongPinRejected = true }
        assertTrue("A different certificate pin must fail before HTTP", wrongPinRejected)
        var unauthenticatedRejected = false
        try { PonteClient(connection).listFiles() }
        catch (_: IOException) { unauthenticatedRejected = true }
        assertTrue("A correct TLS identity is not enough to authorize files", unauthenticatedRejected)
        val paired = PonteClient(connection).pair(code, "Android JVM integration test")
        assertEquals(64, paired.token.length)
        val client = PonteClient(paired.copy(host = "::1"))
        assertEquals("0.7.1", client.pcStatus().version)
        assertTrue(client.pcStatus().fileSessionId.isNotEmpty())
        assertTrue(client.listFiles().isEmpty())
        val bytes = ByteArray(300_123) { (it % 251).toByte() }
        client.upload("ação pelo Kotlin.bin", bytes.size.toLong()) { ByteArrayInputStream(bytes) }
        val files = client.listFiles()
        assertEquals(1, files.size)
        assertEquals("ação pelo Kotlin.bin", files.single().name)
        assertEquals(bytes.size.toLong(), files.single().size)
        val downloaded = ByteArrayOutputStream()
        client.download(files.single()) { downloaded }
        assertArrayEquals(bytes, downloaded.toByteArray())
        // Providers without a known length use chunked streaming.
        client.upload("tamanho-desconhecido.bin", -1) { ByteArrayInputStream(bytes) }
        assertEquals(2, client.listFiles().size)
        val remote = client.openRemote()
        assertEquals(64, remote.sessionId.length)
        val options = client.desktopOptions(remote.sessionId)
        assertEquals(2, options.monitors.size)
        assertFalse(options.audioAllowed)
        client.configureDesktop(remote.sessionId, "left", "fluid")
        assertEquals("left", client.desktopOptions(remote.sessionId).monitorId)
        assertEquals("fluid", client.desktopOptions(remote.sessionId).profile)
        client.sendRemote(remote.sessionId, RemoteCommand("mousedown", .5, .5))
        client.sendRemote(remote.sessionId, RemoteCommand("dragkeepalive"))
        client.sendRemote(remote.sessionId, RemoteCommand("move", .6, .6))
        client.sendRemote(remote.sessionId, RemoteCommand("mouseup", .6, .6))
        val jpeg = client.remoteFrame(remote.sessionId)
        assertArrayEquals(expectedJpeg, jpeg)
        client.sendRemote(remote.sessionId, RemoteCommand("click", 0.5, 0.5))
        client.sendRemote(remote.sessionId, RemoteCommand("text", text = "ação 😀"))
        client.sendRemote(remote.sessionId, RemoteCommand("key", key = "volumeup"))
        client.sendRemote(remote.sessionId, RemoteCommand("scroll", delta = -120))
        assertTrue(client.keyboardTarget(remote.sessionId, 0.5, 0.5).editable)
        client.sendRemote(remote.sessionId, RemoteCommand("edit", delta = 1, text = "á"))
        var invalidCommandRejected = false
        try { client.sendRemote(remote.sessionId, RemoteCommand("key", key = "arbitrary")) }
        catch (e: PonteHttpException) { invalidCommandRejected = e.status == 400 }
        assertTrue("Invalid keys cannot reach the input adapter", invalidCommandRejected)
        client.closeRemote(remote.sessionId)
        var closedSessionRejected = false
        try { client.remoteFrame(remote.sessionId) }
        catch (e: PonteHttpException) { closedSessionRejected = e.status == 409 }
        assertTrue("Closed screen sessions cannot retrieve additional frames", closedSessionRejected)
        val live = client.openRemote()
        var frames = 0
        val started = System.nanoTime()
        withTimeout(25000) {
            client.remoteFrames(live.sessionId).take(240).collect { packet ->
                assertArrayEquals(expectedJpeg, packet.jpeg)
                assertEquals("main", packet.monitorId)
                frames++
                packet.acknowledge()
            }
        }
        assertEquals(240, frames)
        assertTrue("Continuous connection must outlive the old 8-second call timeout", System.nanoTime() - started > 8_000_000_000L)
        val reconnect = client.openRemote()
        assertArrayEquals(jpeg, client.remoteFrame(reconnect.sessionId))
        client.closeRemote(reconnect.sessionId)
        var reusedCodeRejected = false
        try { PonteClient(connection).pair(code, "replay") }
        catch (_: IOException) { reusedCodeRejected = true }
        assertTrue("The pairing credential cannot be reused", reusedCodeRejected)
        // Genuine >1 GiB transfer, bounded memory on both sides, synthetic bytes only.
        val largeSize = 1024L * 1024 * 1024 + 131072
        val sourceCrc = java.util.zip.CRC32()
        client.upload("synthetic-over-1gib.bin", largeSize) {
            object : java.io.InputStream() {
                var remaining = largeSize
                override fun read(): Int {
                    if (remaining == 0L) return -1
                    remaining--; sourceCrc.update(0); return 0
                }
                override fun read(buffer: ByteArray, offset: Int, length: Int): Int {
                    if (remaining == 0L) return -1
                    val count = minOf(remaining, length.toLong()).toInt()
                    java.util.Arrays.fill(buffer, offset, offset + count, 0.toByte())
                    remaining -= count
                    sourceCrc.update(buffer, offset, count)
                    return count
                }
            }
        }
        val large = client.listFiles().single { it.name == "synthetic-over-1gib.bin" }
        assertEquals(largeSize, large.size)
        val targetCrc = java.util.zip.CRC32()
        var receivedBytes = 0L
        client.download(large) {
            object : java.io.OutputStream() {
                override fun write(value: Int) { receivedBytes++; targetCrc.update(value) }
                override fun write(buffer: ByteArray, offset: Int, length: Int) { receivedBytes += length; targetCrc.update(buffer, offset, length) }
            }
        }
        assertEquals(largeSize, receivedBytes)
        assertEquals(sourceCrc.value, targetCrc.value)
    }
}
