package br.com.ponte

import android.annotation.SuppressLint
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.X509TrustManager

// This is direct trust in a locally approved certificate, not CA/PKIX trust.
// TLS proves ownership of its private key; an exact DER SHA-256 pin and validity
// are required. No unpinned certificate or fallback to insecure trust is allowed.
@SuppressLint("CustomX509TrustManager")
internal class PinnedTrustManager(fingerprint: String) : X509TrustManager {
    private val expected: ByteArray
    init {
        require(fingerprint.matches(Regex("[0-9a-fA-F]{64}"))) { "Identidade TLS inválida." }
        expected = fingerprint.chunked(2).map { it.toInt(16).toByte() }.toByteArray()
    }
    override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) { throw CertificateException("Não suportado") }
    override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
        val certificate = chain?.firstOrNull() ?: throw CertificateException("Certificado ausente")
        certificate.checkValidity()
        val actual = MessageDigest.getInstance("SHA-256").digest(certificate.encoded)
        if (!MessageDigest.isEqual(actual, expected)) throw CertificateException("O certificado do PC mudou. Faça o pareamento novamente.")
    }
}
