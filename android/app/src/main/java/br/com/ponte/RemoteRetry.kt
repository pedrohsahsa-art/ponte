package br.com.ponte

import java.io.IOException
import javax.net.ssl.SSLException

object RemoteRetry {
    fun shouldRetry(error: Throwable): Boolean = when (error) {
        is SSLException -> false // never hide certificate/pin errors with an automatic retry
        is PonteHttpException -> error.status in setOf(403, 409, 423, 429, 500, 502, 503, 504, 507)
        is IOException -> true
        else -> false
    }
    fun delayMs(failures: Int): Long = when {
        failures <= 1 -> 500
        failures == 2 -> 1000
        failures == 3 -> 2000
        else -> 5000
    }
}
