package br.com.ponte

import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import java.io.IOException

class NetworkTransitionTest {
    @Test fun replacementThenStaleLossDoesNotCauseDuplicateRestart() {
        val state = DefaultNetworkState("wifi")
        assertFalse(state.available("wifi"))
        assertTrue(state.available("mobile"))
        assertFalse(state.lost("wifi"))
        assertTrue(state.lost("mobile"))
        assertFalse(state.lost("mobile"))
        assertTrue(state.available("wifi"))
    }
    @Test fun wifiMobileWifiKeepsCredentialsAndReturnsToLocal() = runBlocking {
        val initial = Connection("192.168.2.4", 47831, "A".repeat(64), "B".repeat(64), internetHost = "meu-pc.duckdns.org")
        var wifi = true
        suspend fun probe(target: Connection) { if (!wifi && target.host == target.localHost) throw IOException("LAN unreachable") }
        val local = ConnectionRouting.resolve(initial, preferLocal = true, probe = ::probe)
        wifi = false
        val mobile = ConnectionRouting.resolve(local, preferLocal = true, probe = ::probe)
        wifi = true
        val returned = ConnectionRouting.resolve(mobile, preferLocal = true, probe = ::probe)
        assertEquals(initial.localHost, returned.host)
        assertEquals(initial.internetHost, mobile.host)
        assertEquals(initial.token, returned.token)
        assertEquals(initial.fingerprint, returned.fingerprint)
    }
}
