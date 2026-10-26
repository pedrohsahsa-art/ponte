# DDNS e reconexão — versão 0.7

## DDNS gratuito integrado

Provedor: DuckDNS. Referência: https://www.duckdns.org/spec.jsp (consultada em 2026-10-25). O domínio e a conta precisam ser criados pelo usuário em https://www.duckdns.org. O Ponte não cria contas, não registra domínios em nome do usuário e não necessita receber tokens pelo chat.

1. Crie um subdomínio na sua conta DuckDNS.
2. No Ponte Windows, abra **Conexão → Endereço fixo · DuckDNS gratuito**.
3. Informe subdomínio e token no PasswordBox, marque atualização automática e salve.
4. Aguarde a confirmação do provedor. O token não é exibido novamente; campo vazio mantém o token já salvo. Para desligar, desmarque e salve.
5. No Android, em **Ajustes**, use Aprender IPv6 do PC para permitir aprender o hostname confirmado. Domínios configurados manualmente continuam sendo preservados. A identidade TLS e o token de pareamento não mudam.

O serviço atualiza o AAAA pelo endpoint HTTPS oficial e parâmetro `ipv6` explícito, sem depender da autodetecção IPv4. Não utiliza HTTP, não segue redirecionamentos e não aceita URLs de provedores arbitrários. Tenta no início, a cada 5 minutos e após mudanças de endereço, com debounce. Requer o Ponte aberto. O token é salvo em `data/ddns.dpapi`, protegido com DPAPI do usuário Windows, fora da biblioteca temporária; exceções de rede não são copiadas para mensagens de UI para não vazar a URL autenticada.

Uma resposta OK confirma aceitação da atualização pelo provedor, não propagação imediata de DNS nem entrada no roteador. O hostname só é anunciado pelo endpoint autenticado de rede depois de atualização aceita para o IPv6 corrente. Uma mudança do prefixo IPv6 também pode exigir atualizar uma regra do roteador se ela foi configurada para um endereço literal. DDNS não altera firewall e não resolve sozinho essa regra.

## Reconexão

Os viewmodels Android observam a rede padrão do sistema. Troca/perda de rede reinicia a sessão PC-remoto ativa, limpa fila de entrada/arrasto e tenta novamente endereços conhecidos sem mudar identidade. Consultas usam a rede padrão atual, não um Network antigo fixado. Retorno à rede local dá preferência ao endereço LAN.

Áudio do PC solicitado pelo usuário é restabelecido após o primeiro quadro de uma nova conexão; falha de permissão de áudio permanece independente da tela. Bloqueio do PC mantém autorização permanente e gera tentativas limitadas com backoff. Desconectar explicitamente interrompe novas tentativas e desliga a intenção de áudio. Não há replay automático de teclas, arquivos ou gestos antigos. Reconexão não limpa a biblioteca.

Android → PC é uma sessão distinta de MediaProjection: por segurança, perda de rede, rotação e bloqueio do telefone a encerram; é necessário autorizar uma nova captura, não reutilizar silenciosamente o grant.

## Teste físico necessário

1. Autorize controle no PC e abra a tela pelo Wi-Fi.
2. Ative Ouvir PC e confirme som.
3. Sem desconectar pelo app, passe para 4G/5G: deve aparecer reconexão, nova imagem e áudio. Não deve haver tecla/arrasto preso.
4. Volte ao Wi-Fi e confirme Rede local, sem nova aprovação permanente.
5. Bloqueie/desbloqueie o PC: imagem deve pausar/retornar, sem revogar acesso.
6. Faça o mesmo após configurar o domínio DuckDNS. Confirme no painel do provedor ou em consulta AAAA que ele aponta para o IPv6 atual.
7. Envie um arquivo e interrompa a rede: erro deve ser explícito, sem publicação parcial nem reenvio silencioso; a lista não deve ser apagada.

Testes automatizados cobrem protocolo de DDNS com handler sintético, proteção DPAPI real, rejeições, descoberta autenticada e regressões de sessão/roteamento. Não houve atualização real de domínio do usuário sem credenciais, nem simulação de troca física de Wi-Fi/4G pelo computador.
