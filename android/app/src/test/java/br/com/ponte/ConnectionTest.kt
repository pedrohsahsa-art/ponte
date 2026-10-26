package br.com.ponte

import org.junit.Assert.*
import org.junit.Test

class ConnectionTest {
    private val pin = "a1".repeat(32)
    private val code = "B2".repeat(32)
    private val link = "ponte://pair?host=100.70.80.90&port=47831&fingerprint=$pin&code=$code"

    @Test fun validPairing() {
        val (connection, actualCode) = Connection.parsePairing(link)
        assertEquals("100.70.80.90", connection.host)
        assertEquals(47831, connection.port)
        assertEquals(pin.uppercase(), connection.fingerprint)
        assertEquals(code, actualCode)
        assertEquals("", connection.token)
    }
    @Test fun trimsPastedLink() { assertEquals("100.70.80.90", Connection.parsePairing("  $link\n").first.host) }
    @Test fun rejectsHttpLink() { rejected(link.replace("ponte://", "http://")) }
    @Test fun rejectsOtherAuthority() { rejected(link.replace("pair?", "attacker?")) }
    @Test fun rejectsCredentialsInAuthority() { rejected(link.replace("pair?", "user@pair?")) }
    @Test fun rejectsExtraPath() { rejected(link.replace("pair?", "pair/other?")) }
    @Test fun rejectsFragment() { rejected("$link#extra") }
    @Test fun rejectsMissingPin() { rejected(link.replace("&fingerprint=$pin", "")) }
    @Test fun rejectsMalformedPin() { rejected(link.replace(pin, "not-a-pin")) }
    @Test fun rejectsMissingCode() { rejected(link.replace("&code=$code", "")) }
    @Test fun rejectsBadPort() { rejected(link.replace("47831", "65536")); rejected(link.replace("47831", "0")) }
    @Test fun rejectsDuplicateKeys() { rejected("$link&host=192.168.0.5") }
    @Test fun rejectsHostnameAndInjection() {
        rejected(link.replace("100.70.80.90", "example.com"))
        rejected(link.replace("100.70.80.90", "127.0.0.1%2Fother"))
    }
    @Test fun validatesIpv4() {
        assertTrue(Connection.validHost("192.168.1.3"))
        assertTrue(Connection.validHost("100.64.0.1"))
        for (host in listOf("256.1.1.1", "1.2.3", "1.2.3.4.5", "-1.2.3.4", "::1", "1.2.3.a", "1.2.3.", "1.2.3.4:80")) {
            assertFalse(host, Connection.validHost(host))
        }
    }
    private fun rejected(value: String) {
        try { Connection.parsePairing(value); fail("Should reject: $value") }
        catch (_: IllegalArgumentException) { }
    }
}
