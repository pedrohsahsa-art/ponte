package br.com.ponte

import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Assert.*
import org.junit.Test
import java.io.Closeable
import java.io.IOException
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.CopyOnWriteArrayList

class ConnectionDeadlineTest {
    // Accepts TCP but deliberately never completes TLS. No certificate is trusted
    // or bypassed: this reproduces a stalled network/server before HTTP upgrade.
    private class SilentPeer : Closeable {
        val server = ServerSocket(0, 10, InetAddress.getByName("127.0.0.1"))
        private val sockets = CopyOnWriteArrayList<Socket>()
        init {
            Thread {
                try { while (!server.isClosed) sockets.add(server.accept()) }
                catch (_: IOException) { }
            }.apply { isDaemon = true; start() }
        }
        fun client() = PonteClient(Connection("127.0.0.1", server.localPort, "00".repeat(32), "11".repeat(32)))
        override fun close() { server.close(); sockets.forEach { it.close() } }
    }
    @Test fun metadataTimeoutIsIndependentOfUnlimitedFileTransfers() = runBlocking {
        SilentPeer().use { peer ->
            val started = System.nanoTime()
            var failed = false
            try { withTimeout(12000) { peer.client().pcStatus() } }
            catch (_: IOException) { failed = true }
            assertTrue("Metadata request must report a network failure", failed)
            assertTrue("Metadata is bounded independently of long file transfers", System.nanoTime() - started < 10_000_000_000L)
        }
    }
    @Test fun streamCannotLoadForeverBeforeTheFirstFrame() = runBlocking {
        SilentPeer().use { peer ->
            val started = System.nanoTime()
            var failed = false
            try { withTimeout(15000) { peer.client().remoteFrames("22".repeat(32)).first() } }
            catch (_: IOException) { failed = true }
            assertTrue("No first frame must result in a visible failure", failed)
            assertTrue("Startup deadline must fire before an infinite spinner", System.nanoTime() - started < 13_000_000_000L)
        }
    }
}
