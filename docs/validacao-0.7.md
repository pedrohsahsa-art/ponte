# Validação 0.7

## Implementado

- DuckDNS AAAA por HTTPS oficial, sem redirecionamento/HTTP; token DPAPI, edição por PasswordBox e nenhum segredo em mensagens de UI. Conta do usuário ainda não configurada.
- Android observa a troca da rede padrão. Retentativa de sessão, liberação de entrada e áudio solicitado preservado após quedas; negativas explícitas de permissão não geram tentativas de áudio em loop.
- PC → Android com visualizador WPF, captura MediaProjection em foreground service, notificação Parar e comandos Accessibility condicionados à sessão. Sem servidor no telefone, root, ADB ou aprovação de permissões automática.
- UI minimalista por abas/seções, sem misturar a biblioteca com todos os ajustes e com contraste/estados de parada preservados.

## Evidência automatizada

- 166 verificações .NET, incluindo DPAPI real e atualização DuckDNS simulada (sem alterar domínio de ninguém), limites de JPEG/comando, stream reverso real por TLS/IPv6, ACK e corte da sessão, além das regressões de arquivos/consentimento.
- Kotlin/Robolectric: protocolo de comandos e pacotes protegidos, estado de rede padrão e roteiro sintético Wi-Fi/celular/Wi-Fi preservando pin/token. MediaProjection e gestos reais não são simulados por esses testes.
- Compilação Android e lint; integração OkHttp/Kestrel com IPv6, tela sintética e arquivo >1 GiB com CRC/contagem de bytes.
- Smoke do Windows publicado: restauração de permissões, geração de QR, limpeza restrita por sessão e preservação de dados fora da biblioteca.

Os testes de atualização DuckDNS são sintéticos: aceitação da requisição e criptografia local não demonstram publicação real de um AAAA de conta do usuário. O teste de troca de rede usa estados/roteamento simulados, não uma migração física de conexão móvel.

## Não confirmado sem usuário/dispositivo

Não foi criada conta DDNS, atualizado domínio real, suspendido/bloqueado o computador do usuário nem trocada a conexão de um telefone. O aparelho Android não está conectado via ADB. A captura, Accessibility, rotação, notificações e o visual em tela física precisam ser verificados manualmente. A capacidade PC → Android é primeira versão, não uma garantia de compatibilidade com todos os fabricantes/apps.

Guias: `docs/ddns-reconexao.md`, `docs/pc-android.md`, `docs/internet-direta.md`. Build Android versionCode 8; instalar por cima mantém a identidade do APK e os pareamentos.
