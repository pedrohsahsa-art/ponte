package br.com.ponte

import org.junit.Assert.*
import org.junit.Test
import java.io.IOException
import java.net.SocketTimeoutException
import javax.net.ssl.SSLHandshakeException

class RemoteRetryTest {
    @Test fun automaticallyRetriesTemporaryNetworkFailures() {
        assertTrue(RemoteRetry.shouldRetry(IOException("wifi")))
        assertTrue(RemoteRetry.shouldRetry(SocketTimeoutException()))
    }
    @Test fun waitsForPcAuthorizationOrUnlockWithoutRequiringAnotherTap() {
        for (code in listOf(403, 409, 423, 429, 503)) assertTrue(RemoteRetry.shouldRetry(PonteHttpException(code, "temporary")))
    }
    @Test fun neverHidesPinFailuresOrRevokedPairing() {
        assertFalse(RemoteRetry.shouldRetry(SSLHandshakeException("pin mismatch")))
        assertFalse(RemoteRetry.shouldRetry(PonteHttpException(401, "revoked")))
        assertFalse(RemoteRetry.shouldRetry(PonteHttpException(404, "update Windows")))
        assertFalse(RemoteRetry.shouldRetry(IllegalArgumentException("bad jpeg")))
    }
    @Test fun boundedBackoffHasNoRetryCountOrDurationLimit() {
        assertEquals(500, RemoteRetry.delayMs(1))
        assertEquals(1000, RemoteRetry.delayMs(2))
        assertEquals(5000, RemoteRetry.delayMs(10000))
        assertEquals(5000, RemoteRetry.delayMs(Int.MAX_VALUE))
    }
}
