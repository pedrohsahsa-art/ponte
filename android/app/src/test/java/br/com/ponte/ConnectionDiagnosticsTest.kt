package br.com.ponte

import org.junit.Assert.*
import org.junit.Test
import java.net.ConnectException
import java.net.SocketTimeoutException
import javax.net.ssl.SSLHandshakeException

class ConnectionDiagnosticsTest {
    @Test fun internetErrorsPointToRouterWithoutMisformattingIpv6() {
        val message = ConnectionDiagnostics.describe(ConnectException(), "2001:db8::5")
        assertTrue(message.contains("[2001:db8::5]:47831"))
        assertTrue(message.contains("roteador"))
        assertFalse(message.contains("Radmin"))
    }
    @Test fun unreachablePcShowsTargetAndOpenPcInstructions() {
        val message = ConnectionDiagnostics.describe(ConnectException(), "192.168.2.153")
        assertTrue(message.contains("192.168.2.153:47831"))
        assertTrue(message.contains("Abra o Ponte"))
        assertTrue(message.contains("Radmin"))
    }
    @Test fun missingConsentIsNotDescribedAsLostWifi() {
        val message = ConnectionDiagnostics.describe(PonteHttpException(403, "forbidden"), "192.168.2.153")
        assertTrue(message.contains("respondeu"))
        assertTrue(message.contains("Autorizar"))
    }
    @Test fun startupTimeoutRetainsItsReason() {
        val message = ConnectionDiagnostics.describe(SocketTimeoutException("primeiro quadro"), "192.168.2.153")
        assertTrue(message.contains("primeiro quadro"))
        assertTrue(message.contains("firewall"))
    }
    @Test fun certificateFailureNeverSuggestsDisablingValidation() {
        val message = ConnectionDiagnostics.describe(SSLHandshakeException("pin"), "192.168.2.153")
        assertTrue(message.contains("não desative"))
    }
}
