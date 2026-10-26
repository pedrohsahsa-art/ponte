package br.com.ponte

import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import java.io.IOException

class InternetConnectionTest {
    private val connection = Connection("192.168.2.5", 47831, "A".repeat(64), "B".repeat(64), internetHost = "2001:db8::5")
    @Test fun ipv6UrlsUseBracketsAndKeepThePort() {
        val url = NetworkHost.url("2001:db8::5", 47831, "/api/status")
        assertEquals("https://[2001:db8::5]:47831/api/status", url.toString())
        assertEquals("2001:db8::5", url.host)
        assertEquals("2001:db8::5", NetworkHost.internet("[2001:DB8::5]"))
    }
    @Test fun dnsNamesAreCanonicalizedWithoutResolvingOrChangingIdentity() {
        assertEquals("meu-pc.example.org", NetworkHost.internet("MEU-PC.example.org"))
        assertEquals(connection.fingerprint, connection.copy(host = "meu-pc.example.org").fingerprint)
    }
    @Test fun internetFieldRejectsUrlsPortsInjectionAndScopedLocalAddresses() {
        for (host in listOf("https://example.org", "example.org:443", "user@example.org", "2001:db8::5/other", "fe80::1%3", "fe80::1", "::1", "fd00::1", "-bad.example.org", "1.2.3.999")) {
            try { NetworkHost.internet(host); fail(host) } catch (_: IllegalArgumentException) { }
        }
    }
    @Test fun oldSavedConnectionsKeepLocalAddressAndNewFieldsRoundTrip() {
        val legacy = connection.json().apply { remove("localHost"); remove("internetHost") }
        assertEquals(connection.host, Connection.fromJson(legacy).localHost)
        assertEquals("", Connection.fromJson(legacy).internetHost)
        assertEquals(connection, Connection.fromJson(connection.json()))
    }
    @Test fun networkFailureFallsBackWithoutChangingLocalHostTokenOrPin() = runBlocking {
        val calls = mutableListOf<String>()
        val resolved = ConnectionRouting.resolve(connection) { target -> calls += target.host; if (target.host == connection.localHost) throw IOException("unreachable") }
        assertEquals(listOf(connection.localHost, connection.internetHost), calls)
        assertEquals(connection.internetHost, resolved.host)
        assertEquals(connection.localHost, resolved.localHost)
        assertEquals(connection.token, resolved.token)
        assertEquals(connection.fingerprint, resolved.fingerprint)
        assertEquals("Internet direta", resolved.routeLabel)
    }
    @Test fun returningToWifiPrefersLocalWhenRequested() = runBlocking {
        val resolved = ConnectionRouting.resolve(connection.copy(host = connection.internetHost), preferLocal = true) { }
        assertEquals(connection.localHost, resolved.host)
    }
    @Test fun authorizationFailureIsNotBypassedByTryingAnotherEndpoint() = runBlocking {
        var count = 0
        try { ConnectionRouting.resolve(connection) { count++; throw PonteHttpException(403, "denied") }; fail() }
        catch (_: PonteHttpException) { assertEquals(1, count) }
    }
}
