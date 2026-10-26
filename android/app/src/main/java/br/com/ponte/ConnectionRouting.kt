package br.com.ponte

import kotlinx.coroutines.CancellationException

object ConnectionRouting {
    // Probe only status: never replay input or file uploads across endpoints.
    suspend fun resolve(connection: Connection, preferLocal: Boolean = false, probe: suspend (Connection) -> Unit = { PonteClient(it).pcStatus(); Unit }): Connection {
        val hosts = (if (preferLocal) listOf(connection.localHost, connection.host, connection.internetHost) else listOf(connection.host, connection.localHost, connection.internetHost))
            .filter { it.isNotEmpty() }.distinct()
        var failure: Exception? = null
        for (host in hosts) {
            val candidate = connection.copy(host = host)
            try { probe(candidate); return candidate }
            catch (e: CancellationException) { throw e }
            catch (e: PonteHttpException) { throw e } // explicit auth/policy denial must not be bypassed
            catch (e: Exception) { failure = e }
        }
        throw failure ?: IllegalStateException("Nenhum endereço do PC configurado.")
    }
}
