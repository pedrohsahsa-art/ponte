# Validação 0.6 — internet direta

## Escopo

Windows 0.6.0, APK versionCode 7. Nenhuma alteração automática de roteador, encaminhamento, DMZ ou regras do firewall Windows. Sem conta, VPN externa, retransmissor ou mensalidade do Ponte. Rede móvel e DDNS, se escolhido, seguem as condições dos respectivos provedores.

## Testes

- 142 verificações .NET: inclui política de LAN/internet, bloqueio de pareamento público mesmo com opt-in, autenticação sobre socket IPv6 real e endpoint de endereço externo desligado por padrão, além das regressões anteriores.
- Kotlin: montagem de URL IPv6 com colchetes, canonicalização de domínio sem DNS na validação, rejeição de esquema/caminho/credenciais/porta e endereços IPv6 link-local/privados no campo de internet; migração de conexão antiga; resolução de endpoints preservando token/pin/localHost; preferência local ao retornar; negativas explícitas de autenticação não são contornadas.
- Integração OkHttp/Kestrel passa a usar `::1` após parear por IPv4: status, arquivos, sessão, stream e comandos atravessam IPv6 com pin e bearer. Transfere >1 GiB sintético com contagem/CRC, sem transmitir o desktop real.
- Smoke WPF verifica checkbox independente de internet desligado por padrão ou restaurado de uma preferência semeada em diretório isolado (`-WithInternet`).
- Sonda do PC aceita IPv4/IPv6 via UriBuilder e continua exigindo certificado exato e HTTP 401 ao consultar sem bearer.

## Ainda depende de teste físico

O usuário confirmou IPv6 no telefone; o PC tem IPv6 global e saída IPv6 funcional. Isso não prova entrada pelo firewall do roteador. O teste pelo 4G/5G precisa ser feito após atualizar o APK e habilitar o acesso no Windows. Prefixo residencial dinâmico, mudança Wi-Fi/celular e DDNS real não são simulados por loopback.

Não foi contratado/configurado DDNS e não houve acesso administrativo ao roteador. Endereço manual não é sobrescrito pela aprendizagem automática; o botão Aprender IPv6 permite voltar a ela. Sem DDNS atualizado, uma mudança do prefixo IPv6 pode exigir reaprender o endereço ainda na LAN.
