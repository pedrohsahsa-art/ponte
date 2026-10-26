# Protocolo Ponte 0.7

### Diagnóstico 0.7.1

Além do JPEG binário, `/api/android/stream` recebe mensagens texto de até 1024 bytes `{ "type":"controlStatus", "code":"accessibility_disabled|protected_window|ready|..." }`. Somente códigos conhecidos são convertidos em mensagens fixas do PC; não são aceitos textos arbitrários nem conteúdo de campos. Não há ACK para feedback; ACK continua exclusivo de JPEG validado/decodificado. Tap/swipe/text permanecem bloqueados em interfaces protegidas; Início/Voltar são navegação de saída e continuam exigindo compartilhamento autorizado e telefone desbloqueado.

## Extensões 0.7

`GET /api/network/internet` inclui `hostname` opcional, anunciado somente após resposta DuckDNS aceita para o IPv6 corrente e com acesso à internet habilitado. Token DDNS nunca é retornado. O atualizador usa o HTTPS público oficial do DuckDNS com confiança PKI normal, separado do cliente com pin do Ponte.

`GET /api/android/stream` exige bearer e visualizador Windows aberto explicitamente para o dispositivo correspondente. Android inicia o upgrade e envia JPEG binário de no máximo 2 MiB, dimensões até 1920×1920 e área até 2.073.600 pixels. Servidor valida SOF antes de decodificar e responde texto JSON `{ "kind":"ack" }`. Android mantém um quadro em voo, encerra após 8 segundos sem ACK; servidor limita inatividade a 12 segundos.

No mesmo socket o PC pode enviar `tap` (x/y), `swipe` (x/y/endX/endY), `back`, `home`, `recents` e `text` (substituição de até 1000 caracteres do campo focado). Coordenadas normalizadas, finitas, em [0,1]. A entrada passa por filtro de sessão MediaProjection ativa, desbloqueio e pacote ativo no serviço Android. Fechar/revogar/interromper encerra transporte. Não há API HTTP pública de injeção PC → Android; comandos vêm do visualizador local, não de outro telefone pareado.

Perda de rede no controle Android → PC existente restaura sessão com credenciais novas, sem replay de entrada. Já o compartilhamento PC → Android termina e exige uma nova autorização MediaProjection, inclusive após bloqueio/rotação.

## Internet direta 0.6

URLs IPv6 usam autoridade com colchetes; Android monta HTTP/WS via `HttpUrl` e mantém pin TLS e bearer inalterados. Conexão cifrada inclui `localHost`, `internetHost` e `learnInternet`, além do `host` ativo, com migração de registros antigos. Nomes DDNS são aceitos somente como hostname, sem esquema, credenciais, porta ou caminho.

`GET /api/network/internet`, autenticado, retorna `{ "enabled":false, "ipv6":[], "port":47831 }` ou lista de IPv6 globais não temporários quando internet está permitida no PC. Não abre portas e não confirma alcançabilidade externa.

Servidor rejeita `/pair` fora da sub-rede local mesmo com internet habilitada. APIs externas dependem de opt-in do Windows, além das permissões anteriores. A origem é o IP real do socket; cabeçalhos de proxy não são autoridade. Não há servidor intermediário nem retomada automática de upload/comandos após mudança de rede.

## Extensões 0.5

- `GET /api/remote/options`: exige bearer + sessão autorizada; retorna `monitors` (`id`, `name`, `left`, `top`, `width`, `height`, `primary`), `settings` (`monitorId`, `profile`) e `audioAllowed`.
- `POST /api/remote/options`: aplica `{ "monitorId": "ID do monitor ou vazio para principal", "profile": "fluid|balanced|sharp" }`. Monitor desconhecido ou perfil inválido resulta em 400. Trocar monitor libera arrastos. Coordenadas de entrada e consulta de teclado são relativas ao monitor selecionado, inclusive em áreas negativas da tela virtual.
- Cada JPEG do WebSocket é precedido de mensagem texto JSON `{ "type":"frame", "monitorId":"...", "width":1280, "height":720 }`. O ACK binário `0x01` continua sendo enviado somente depois de decodificar (ou descartar um quadro de monitor antigo). Sem fila de vídeo. Perfis: fluid até 960×540/JPEG42/15fps; balanced até 1280×720/JPEG56/20fps; sharp até 1920×1080/JPEG72/20fps. Mantém proporção e não amplia a fonte.
- Comandos `mousedown`, `move`, `mouseup` usam coordenadas normalizadas; `dragkeepalive` renova a vida do arrasto sem injetar entrada. Heartbeat Android a cada 400 ms; watchdog Windows libera após 2 s sem renovação. Não há modificadores arbitrários mantidos.
- `GET /api/remote/audio` faz upgrade para WebSocket após bearer, sessão e permissão local separada. Primeira mensagem JSON `{ "sampleRate":48000, "channels":2, "bits":16 }` (sampleRate acompanha o dispositivo). Binários seguintes são PCM16 little-endian estéreo; mono é duplicado, multicanal usa L/R frontais. Fila limitada, erro no receptor lento; revogação/suspensão encerra áudio. Não captura microfone.
- `GET /api/network/wake` exige pareamento; retorna lista de `{ "mac":"12 dígitos hex", "broadcast":"IPv4" }` da interface usada na conexão, sem habilitar WoL no sistema. Android armazena junto à conexão cifrada e envia magic packet de 102 bytes via UDP 9 somente por ação explícita.
- Descoberta LAN: UDP 47832, pedido ASCII `PONTE_DISCOVER ` + nonce hexadecimal de 32 caracteres. Resposta JSON `{ "nonce":"...", "fingerprint":"SHA256 certificado", "port":47831 }`, sem bearer/MAC/código de pareamento. Android compara nonce/pin/porta, usa IP de origem e valida HTTPS com pin exato antes de aceitar/salvar. Anúncio não autoriza acesso e não altera o pin. IP manual permanece disponível.

Transporte: HTTPS, porta TCP `47831`. Nenhum endpoint HTTP sem TLS. O Android não segue redirecionamentos.

## Identidade e pareamento

O Windows mantém uma identidade TLS autoassinada. O QR Code inclui o SHA-256 do certificado DER completo:

```text
ponte://pair?host=192.168.1.5&port=47831&fingerprint=HEX_SHA256&code=HEX_RANDOM
```

`fingerprint` e `code` têm 64 caracteres hexadecimais. O código é gerado com 32 bytes aleatórios, expira em cinco minutos, só pode ser consumido uma vez e é invalidado pela geração de outro código. A validação e o consumo são atômicos. Uma recusa ou expiração da aprovação exige outro código.

O Android estabelece TLS aceitando **somente** o certificado com esse SHA-256 e dentro da sua validade. Não se deve substituir a validação por um `TrustManager` irrestrito. O certificado substitui a confiança por uma CA pública; o IP pode mudar sem alterar sua identidade.

### `POST /pair`

Sem bearer token; exige código válido, está sujeito a limite de cinco tentativas por IP/minuto e requer aprovação local no PC por até 60 segundos.

```json
{"code":"64 caracteres hexadecimais","name":"Meu Android"}
```

Resposta:

```json
{"token":"64 caracteres hexadecimais","deviceId":"GUID sem hífens","pcName":"NOME-DO-PC"}
```

O token tem 256 bits aleatórios. O Windows guarda apenas o SHA-256. O Android guarda a conexão cifrada com AES-GCM e uma chave do Android Keystore. O token nunca aparece em query strings.

## Endpoints autenticados

Cabeçalho obrigatório: `Authorization: Bearer TOKEN`.

### `GET /api/status`

```json
{"pcName":"NOME-DO-PC","version":"0.5.0","maxFileBytes":null,"fileSession":"pc-restart","fileSessionId":"GUID da sessão do PC","liveKeyboard":true}
```

### `GET /api/files`

Lista da biblioteca, da mais recente para a mais antiga:

```json
[{"id":"GUID sem hífens","name":"foto.png","size":12345,"createdAt":"2026-10-24T12:00:00+00:00","source":"Android"}]
```

### `POST /api/files?name=foto.png`

Corpo binário bruto, `Content-Type: application/octet-stream`, não multipart. O parâmetro `name` deve ser codificado como query string. Não há teto de tamanho do aplicativo: o corpo é transmitido para disco em blocos pequenos, inclusive se o tamanho não for conhecido. Contadores e metadados usam 64 bits; falhas de armazenamento continuam sendo reportadas. Pedidos de controle/pareamento continuam limitados a 8192 bytes, separadamente.

Resposta: metadados do arquivo criado, no mesmo formato de um item da listagem.

O servidor usa GUIDs como chaves de armazenamento, sanitiza o nome de exibição e só publica os metadados depois que o upload termina. O cliente não escolhe o caminho de destino.

### `GET /api/files/{id}`

Retorna `application/octet-stream` com nome de download. Suporta `Range` e respostas `206 Partial Content`. Esta versão Android sempre baixa do início; não há retomada automática.

Ao iniciar uma nova execução do programa Windows, antes de aceitar requisições, a biblioteca elimina somente seus artefatos `GUID.bin`, `GUID.json`, `GUID.partial` e `GUID.json.tmp` no nível superior de `data/files`. Não segue links/junções nem percorre subpastas, e não remove originais, cópias exportadas ou arquivos de autenticação. Reconexão do telefone não limpa a biblioteca. O Android observa `fileSessionId` e atualiza a listagem na tela de arquivos.

## Erros e limites

- `400`: solicitação ou nome inválido.
- `401`: código de pareamento inválido/expirado ou bearer token inválido/revogado.
- `403`: aprovação recusada ou expirada.
- `404`: arquivo inexistente ou identificador inválido.
- `408`: cliente desconectou antes de concluir o pareamento.
- `413`: corpo maior que o limite.
- `409`: conexão de tela inválida, substituída ou encerrada (sem expiração por tempo).
- `423`: área de trabalho indisponível/protegida ou falha de captura/injeção; a autorização permanente é mantida.
- `429`: limite de requisições atingido.
- `503`: recurso indisponível no servidor.
- `507`: falha de E/S na biblioteca, inclusive falta de espaço.

O corpo de erros não é uma API de diagnóstico e pode estar vazio. Requisições `/api` são autenticadas antes da execução da operação. Revogação vale para novas requisições; não cancela uma transferência já autorizada. Uploads para a biblioteca têm concorrência máxima de três operações de escrita.

## Tela e entrada remota

Todos os endpoints abaixo exigem bearer token. Além disso, o usuário do Windows precisa autorizar explicitamente **um deviceId** na interface local. Essa autorização não pode ser criada por HTTP e é salva atomicamente em `data/remote-permission.json`, sem prazo de vencimento. Só a revogação manual a remove; suspensões e encerramento do processo não a apagam. O telefone autorizado recebe uma sessão de 256 bits, apenas em memória, que acompanha frames e comandos no cabeçalho **`X-Ponte-Session`**; nunca na URL.

### `GET /api/remote/status`

```json
{"enabled":true,"connected":false,"expiresAt":null}
```

Refere-se apenas ao telefone que fez a requisição; não divulga dados de outros telefones. `enabled=false` é o padrão.

### `POST /api/remote/session`

Sem corpo. Exige autorização vigente no PC. Uma nova sessão invalida imediatamente a anterior:

```json
{"sessionId":"64 caracteres hexadecimais","expiresAt":null,"intervalMs":50}
```

### `GET /api/remote/frame`

Endpoint de quadro único mantido para diagnóstico/testes. Retorna `image/jpeg`, `Cache-Control: no-store`. Monitor/perfil selecionados, proporção original, cursor incluído e sem gravação em disco. Limite de 12 requisições/segundo por IP. Android usa o stream abaixo, não polling deste endpoint. Atualize Windows e Android juntos.

### `GET /api/remote/stream` — WebSocket TLS

Handshake autenticado pelos mesmos cabeçalhos bearer/sessão, nunca por query string. Antes do upgrade, erros usam os códigos HTTP acima. Após o upgrade:

1. O servidor captura e envia metadados texto do monitor, seguidos de **uma mensagem binária = um JPEG inteiro**, com tamanho máximo de 4 MiB.
2. O Android decodifica, atualiza a imagem e responde **uma mensagem binária de um byte `0x01`**.
3. Só após o ACK o servidor captura o próximo quadro. Não há fila acumulada de frames velhos. O limite de envio é 20 fps (orçamento mínimo de 50 ms incluindo captura/envio/ACK/decodificação).
4. ACKs de outro tipo/tamanho são rejeitados. Uma espera de ACK acima de 15 segundos fecha apenas o transporte, sem retirar a autorização. Pings de saúde ocorrem a cada 10 segundos. O cliente não aplica o antigo timeout HTTP de oito segundos à conexão de vídeo.
5. Cada novo quadro revalida a sessão, a autorização local e o token de pareamento. Revogação manual impede o próximo quadro. No encerramento do transporte, apenas a conexão efêmera é liberada.

Razões de fechamento incluem `access_revoked`, `session_replaced`, `desktop_unavailable`, `invalid_ack`, `frame_too_large` e `stream_ended`. O Android reconecta após falhas transitórias com backoff limitado a 5 segundos e sem limite de tentativas/duração. Erro de pin/certificado ou pareamento revogado não é ignorado. Comandos continuam em HTTP separado e **não são reaplicados** durante a reconexão.

### `POST /api/remote/input`

Exige bearer e sessão. Resposta `204`. Corpo JSON de até 8192 bytes. Tipos aceitos:

```json
{"kind":"move","x":0.5,"y":0.5}
{"kind":"click","x":0.5,"y":0.5}
{"kind":"rightclick","x":0.5,"y":0.5}
{"kind":"doubleclick","x":0.5,"y":0.5}
{"kind":"scroll","delta":-120}
{"kind":"text","text":"Olá, PC!"}
{"kind":"edit","delta":1,"text":"á"}
{"kind":"key","key":"enter"}
```

Coordenadas finitas e normalizadas em `[0,1]`, relativas à imagem/monitor selecionado; o cliente deve excluir barras laterais/superiores do cálculo. Scroll entre -1200 e 1200, diferente de zero e múltiplo de 120. Texto com 1–500 caracteres UTF-16, sem caracteres de controle. Teclas permitidas:

```text
enter backspace tab escape delete left right up down home end pageup pagedown
copy paste selectall undo alttab volumeup volumedown mute
```

Não há comandos de shell nem teclas virtuais arbitrárias. Somente o botão esquerdo pode ser mantido para arrasto, limitado à sessão e ao watchdog; modificadores não são mantidos. A entrada é serializada no servidor. O cliente mantém fila limitada, combina movimentos e não repete requisições de comandos automaticamente. Limite de 80 comandos/segundo por IP.

`edit` aplica de zero a 500 backspaces (`delta`) e depois insere de zero a 500 caracteres UTF-16 (`text`), na mesma operação nativa ordenada; pelo menos uma alteração é exigida. Permite `\n`/`\t`, convertidos em Enter/Tab, mas não outros caracteres de controle. O IME compara somente seu próprio contexto recente, reconstrói o sufixo alterado no caret remoto e divide lotes sem cortar pares substitutos Unicode. Não envia nem solicita o valor completo do campo Windows. HTTP 429, rejeitado antes da execução, pode ser repetido com backoff; resposta perdida de um comando nunca é repetida automaticamente.

### `GET /api/remote/keyboard?x=0.5&y=0.5`

Exige bearer, autorização de controle e sessão. Coordenadas opcionais, em par, normalizadas no monitor selecionado. Consulta apenas metadados do elemento focado e, se houver ponto, exige que ele pertença à área do campo. Resposta:

```json
{"editable":true,"password":false,"focusId":"hash de identificadores de acessibilidade","available":true}
```

Nenhum texto, Value, Name, rótulo ou conteúdo de senha é lido/retornado. UI Automation roda em uma única thread separada com fila limitada; consulta fica limitada a 350 ms, sem bloquear a trava de captura/injeção. Falhas retornam suporte indisponível, não derrubam a transmissão. A sessão é revalidada antes e depois da consulta. O telefone consulta após o clique chegar ao PC e ignora resultados de cliques/sessões antigos. O botão manual permanece disponível.

### `DELETE /api/remote/session`

Exige bearer e sessão. Encerra apenas a conexão atual, não a autorização permanente. Resposta `204`. Uma sessão antiga não pode encerrar uma nova conexão.

### Interrupção

Captura, injeção, troca de sessão e revogação são serializadas pela mesma trava. Depois que o botão Parar retorna, pedidos pendentes com credenciais antigas são rejeitados, mesmo que o usuário autorize novamente. Respostas/quadros que já estavam em trânsito não podem ser recolhidos.

Não há expiração por tempo ocioso nem limite de duração de autorização. Bloqueio/logoff/desconexão do Windows e encerramento do Ponte interrompem a conexão, mas preservam a autorização. Desktop seguro continua inacessível. Falha de captura/injeção não revoga a permissão. Caixas de confirmação locais usam uma suspensão temporária; dispensá-las permite reconectar sem nova autorização do telefone já autorizado.

O viewport Android usa a mesma transformação inversa de escala/translação para cliques que usa para desenhar a imagem. Ajustar preserva o monitor inteiro; Preencher faz zoom proporcional e pode recortar bordas. Zoom/pan são locais ao telefone; as coordenadas enviadas continuam normalizadas em `[0,1]` no monitor original.
