package br.com.ponte

import okhttp3.HttpUrl

object NetworkHost {
    fun canonical(value: String): String {
        val host = value.trim().removeSurrounding("[", "]")
        require(host.isNotEmpty() && host.length <= 253 && host.none { it.isWhitespace() || it in "/\\@?#%" }) { "Informe somente o endereço, sem https://, porta ou caminho." }
        require(if (':' in host) host.all { it in "0123456789abcdefABCDEF:" } else
            Connection.validHost(host) || host.split('.').let { labels -> labels.size >= 2 && labels.all { it.isNotEmpty() && it.length <= 63 && it.first().isLetterOrDigit() && it.last().isLetterOrDigit() && it.all { c -> c in 'a'..'z' || c in 'A'..'Z' || c in '0'..'9' || c == '-' } } && host.any { it in 'a'..'z' || it in 'A'..'Z' } }) { "Endereço IPv4, IPv6 ou domínio inválido." }
        return HttpUrl.Builder().scheme("https").host(host).build().host
    }
    fun internet(value: String): String {
        val host = canonical(value)
        require(':' !in host || host.first() in "23") { "Use um IPv6 público global, não um endereço local/link-local." }
        return host
    }
    fun url(host: String, port: Int, path: String): HttpUrl = HttpUrl.Builder().scheme("https").host(canonical(host)).port(port).encodedPath(path).build()
    fun display(host: String, port: Int) = "${if (':' in host) "[$host]" else host}:$port"
}
