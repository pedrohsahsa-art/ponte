# Internet direta sem mensalidade — Ponte 0.6

## O que esta versão faz

Não usa VPN, servidor de retransmissão nem aplicativo externo. Android e Windows se conectam diretamente por HTTPS/WebSocket com a identidade TLS já pareada. IPv6 público nos dois lados evita a necessidade de IPv4 público/saída de CGNAT, mas não elimina o firewall do roteador.

- Endereço local e endereço de internet são guardados separadamente na conexão cifrada do Android.
- Ao iniciar a tela remota ou voltar à biblioteca, tenta a rede local primeiro. Após perda de rede, tenta os endereços configurados novamente; não há continuidade perfeita de um socket ao trocar de Wi-Fi para celular.
- O caminho ativo é indicado como Rede local ou Internet direta. A primeira conexão remota pela internet começa no perfil Fluidez se qualidade automática estiver ligada.
- Ações de entrada e transferências não são repetidas automaticamente em outro endereço. Uma transferência interrompida precisa ser reiniciada pelo usuário.
- Domínio DDNS é opcional. A versão 0.7 integra atualização DuckDNS depois que o usuário cria conta e informa o token no Windows; a versão 0.6 apenas aceitava o nome manualmente. Não há promessa de acesso quando o prefixo IPv6 da residência mudar sem atualização do nome/endereço e eventual regra do roteador.

## Como configurar

1. Atualize Windows e APK, instalando o APK por cima. Pareamento existente continua válido.
2. No Windows, na coluna Conectar, marque **Permitir dispositivos já pareados pela internet**. A escolha fica salva. Instalações novas começam com a opção desligada.
3. Ainda no Wi-Fi de casa, abra o Android e toque em **Atualizar**. Pelo HTTPS autenticado ele aprende o IPv6 público não temporário do PC, se encontrado. Confira em **Sua conexão → Ajustes → Internet direta**.
4. Se necessário, cole manualmente o IPv6 do Windows (com ou sem colchetes, sem porta/https/caminho) ou um domínio DDNS e toque em **Verificar e salvar**. Uma configuração manual só é salva após resposta autenticada com o pin original.
   Endereço manual ou removido não é sobrescrito pela aprendizagem automática. Para voltar ao modo de aprendizagem, use **Aprender IPv6 do PC**.
5. Desligue o Wi-Fi do telefone e tente **Atualizar** e **Abrir controle remoto** usando 4G/5G.
6. Se houver timeout/sem rota, o roteador pode estar bloqueando entrada IPv6. A regra deve permitir somente **TCP 47831 para o IPv6 deste PC**, nunca DMZ, desligamento total do firewall ou abertura para todos os dispositivos. IPv6 normalmente usa regra de entrada, não NAT/encaminhamento de IPv4. Os menus variam por roteador/provedor; não houve alteração automática.
7. O firewall Windows também precisa permitir o executável/porta para origem externa. Regras antigas limitadas a LocalSubnet não bastam. Esta atualização não altera regras existentes nem adiciona uma regra pública automaticamente.

O teste feito a partir do próprio PC não comprova entrada pelo roteador. O teste decisivo é pelo celular fora do Wi-Fi.

## Segurança e limites

- Pareamento é permitido somente em loopback ou na sub-rede de uma interface local física ativa, comparando o endereço real da conexão, nunca Host/X-Forwarded-For. IPv6 global com prefixo de rede diferente não é considerado LAN. Pela internet só entram dispositivos previamente pareados.
- A opção de internet bloqueia novas requisições externas quando desmarcada e interrompe sessões de controle existentes. Transferências de arquivo já em andamento não têm cancelamento retroativo garantido.
- Todo endpoint `/api` continua exigindo bearer, incluindo status e descoberta do endereço externo. Tela/áudio/entrada exigem também autorização local e sessão; áudio conserva seu opt-in separado.
- Pin TLS exato é preservado inclusive com DDNS. Um IP/domínio apontando para outro computador nunca substitui a identidade salva. Não desative validação de certificado.
- Limites: 128 conexões Kestrel, 8 upgrades WebSocket, 32 requisições simultâneas, cabeçalhos até 16 KiB e timeout de cabeçalhos de 10 s. Controle e pareamento mantêm corpo pequeno; arquivos continuam sem teto fixo do aplicativo. Isso não substitui proteção contra DDoS na rede/provedor.
- Desativar internet não revoga o pareamento permanente. Use Parar/revogar ou revogue o telefone para retirar acesso autorizado.
- O PC precisa estar ligado, conectado e com o Ponte aberto. Wake-on-LAN do aplicativo é local; o PC desligado não recebe comandos HTTPS.
- JPEG e PCM podem consumir muitos dados móveis. O perfil Fluidez reduz imagem, não torna o tráfego gratuito. Áudio PCM48 estéreo usa aproximadamente 1,5 Mbit/s antes de overhead.

## Validação

Testes puros cobrem montagem/canonicalização IPv6, migração de credenciais, domínio DDNS, falhas entre endereços e preservação de pin/token. Testes .NET cobrem comparação de sub-redes IPv4/IPv6, opt-in de internet e bloqueio de pareamento público. Integração real OkHttp/Kestrel passa pelo loopback IPv6 `::1`, sem enviar a tela do usuário, incluindo arquivos e WebSocket. Ainda é necessário validar o caminho real 4G/5G → roteador → PC.
