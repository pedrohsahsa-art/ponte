package br.com.ponte

import java.io.IOException
import java.net.ConnectException
import java.net.NoRouteToHostException
import java.net.SocketTimeoutException
import javax.net.ssl.SSLException

object ConnectionDiagnostics {
    fun describe(error: Throwable, host: String?, port: Int = 47831): String {
        val target = host?.let { NetworkHost.display(it, port) } ?: "o PC"
        val internet = host != null && (':' in host || host.any { it.isLetter() })
        return when (error) {
            is PonteHttpException -> when (error.status) {
                401 -> "$target respondeu, mas o pareamento está inválido/revogado. Faça novo pareamento."
                403 -> "$target respondeu. Confira Autorizar telefone no Windows e, fora de casa, a opção Permitir dispositivos já pareados pela internet."
                404 -> "$target não tem este recurso. Atualize também o programa Windows."
                423 -> "$target está bloqueado ou com a área de trabalho indisponível. Sua autorização foi mantida."
                else -> "$target: ${error.message}"
            }
            is SSLException -> "A identidade HTTPS de $target não foi confirmada. Confira a data dos dispositivos e o pareamento; não desative a verificação do certificado."
            is ConnectException, is NoRouteToHostException -> if (internet) "Não consegui chegar a $target. Abra o Ponte no PC e confira IPv6/rota, endereço DDNS e firewall IPv6 do roteador (TCP $port). Não desative o firewall inteiro." else "Não consegui chegar a $target. Abra o Ponte no PC e confira o IP da rede local no Windows (não o IP do Radmin/VPN)."
            is SocketTimeoutException -> "$target demorou a responder. Confira o endereço, o firewall${if (internet) " IPv6 do roteador" else ""} e se o Ponte está aberto. ${error.message ?: ""}"
            is IOException -> "Conexão com $target interrompida: ${error.message ?: "falha de rede"}. Confira o IP local e se o Ponte está aberto."
            else -> error.message ?: "Não foi possível conectar a $target."
        }
    }
}
