package br.com.ponte

import org.junit.Assert.*
import org.junit.Test
import java.math.BigInteger
import java.security.Principal
import java.security.PublicKey
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.CertificateExpiredException
import java.security.cert.X509Certificate
import java.util.Date

class PinnedTrustManagerTest {
    private val certificate = TestCertificate()
    private val fingerprint = MessageDigest.getInstance("SHA-256").digest(certificate.encoded).joinToString("") { "%02x".format(it) }

    @Test fun acceptsExactCertificatePin() { PinnedTrustManager(fingerprint).checkServerTrusted(arrayOf(certificate), "RSA") }
    @Test fun rejectsDifferentCertificatePin() {
        rejected { PinnedTrustManager("00".repeat(32)).checkServerTrusted(arrayOf(certificate), "RSA") }
    }
    @Test fun rejectsExpiredEvenWhenPinMatches() {
        certificate.expired = true
        rejected { PinnedTrustManager(fingerprint).checkServerTrusted(arrayOf(certificate), "RSA") }
    }
    @Test fun rejectsEmptyChain() { rejected { PinnedTrustManager(fingerprint).checkServerTrusted(emptyArray(), "RSA") } }
    @Test fun rejectsMissingChain() { rejected { PinnedTrustManager(fingerprint).checkServerTrusted(null, "RSA") } }
    @Test fun rejectsClientCertificates() { rejected { PinnedTrustManager(fingerprint).checkClientTrusted(arrayOf(certificate), "RSA") } }
    @Test fun rejectsMalformedPin() {
        try { PinnedTrustManager("bad"); fail("Invalid pin accepted") }
        catch (_: IllegalArgumentException) { }
    }
    private fun rejected(action: () -> Unit) {
        try { action(); fail("Untrusted certificate accepted") }
        catch (_: CertificateException) { }
    }

    // Isolates the pin/expiry policy. Actual TLS and key ownership are exercised
    // by the Windows HTTPS integration tests, not by this certificate test double.
    private class TestCertificate : X509Certificate() {
        var expired = false
        override fun checkValidity() { if (expired) throw CertificateExpiredException() }
        override fun checkValidity(date: Date?) = checkValidity()
        override fun getEncoded() = byteArrayOf(1, 2, 3, 4, 5)
        override fun getVersion() = 3
        override fun getSerialNumber(): BigInteger = BigInteger.ONE
        override fun getIssuerDN(): Principal = Principal { "test" }
        override fun getSubjectDN(): Principal = Principal { "test" }
        override fun getNotBefore() = Date(0)
        override fun getNotAfter() = Date(Long.MAX_VALUE)
        override fun getTBSCertificate() = encoded
        override fun getSignature() = byteArrayOf()
        override fun getSigAlgName() = "SHA256withRSA"
        override fun getSigAlgOID() = "1.2.840.113549.1.1.11"
        override fun getSigAlgParams(): ByteArray? = null
        override fun getIssuerUniqueID(): BooleanArray? = null
        override fun getSubjectUniqueID(): BooleanArray? = null
        override fun getKeyUsage(): BooleanArray? = null
        override fun getBasicConstraints() = -1
        override fun verify(key: PublicKey?) { }
        override fun verify(key: PublicKey?, provider: String?) { }
        override fun toString() = "TestCertificate"
        override fun getPublicKey(): PublicKey = error("Not used in policy test")
        override fun hasUnsupportedCriticalExtension() = false
        override fun getCriticalExtensionOIDs(): Set<String>? = null
        override fun getNonCriticalExtensionOIDs(): Set<String>? = null
        override fun getExtensionValue(oid: String?): ByteArray? = null
    }
}
