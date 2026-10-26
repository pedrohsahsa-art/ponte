# Versão 0.4 — sessão de arquivos e teclado ao vivo

## Decisões confirmadas pelo usuário

- Nova sessão significa **fechar e reabrir o Ponte no PC**, não reconectar o telefone.
- Nova sessão deve **apagar as cópias internas** e a listagem, sem apagar originais ou cópias salvas fora da biblioteca.
- Remover o teto de 1 GiB do aplicativo.
- Abrir o teclado Android ao clicar em campos editáveis e transmitir digitação/apagamento em tempo real.

## Implementação

- `FileLibrary.BeginNewSession` é chamado somente depois de obter a trava de instância do programa Windows e antes de iniciar o servidor. Limpa apenas artefatos de nome GUID reconhecidos na raiz de `data/files`; não percorre subpastas nem segue links/junções. Arquivos de identidade, pareamento e autorização ficam fora dessa pasta e não são alterados.
- Sem limite fixo no Kestrel para arquivos, no cliente ou no armazenamento da biblioteca. Fluxo em blocos e contadores de 64 bits. Limites pequenos para requisições de controle/pareamento continuam existindo. Transferências não têm duração absoluta de 30 minutos; preservam timeout de rede sem atividade. Espaço livre, permissões e limites do sistema de arquivos continuam aplicáveis.
- `fileSessionId` em `/api/status` permite ao Android limpar o cache da listagem e atualizar os arquivos automaticamente na tela de biblioteca.
- Após um clique remoto confirmado, o Android consulta `/api/remote/keyboard`, com autenticação, sessão e coordenadas. Windows usa UI Automation em thread separada, com fila limitada e espera de até 350 ms; lê só metadados de foco, editabilidade, área e senha, nunca Value/Name/Text do campo.
- A ponte IME Android usa editor nativo transparente, sem estado salvo/autofill/exportação de acessibilidade. Inserção, composição de acentos, correção e apagamento geram lotes `edit` ordenados de backspaces + texto, sem botão Enviar. Backspace também funciona com contexto local vazio, para apagar texto preexistente no PC sem lê-lo.
- Lotes limitados a 500 caracteres por requisição, divisão sem cortar pares Unicode, fila limitada e sem replay cego após perda de resposta. Apenas HTTP 429 (rejeição antes da execução) pode ser repetido com backoff. Contexto local é descartado ao trocar de campo, ocultar ou desconectar.
- Campo de senha é identificado por metadados. A opção manual usa modo conservador de senha. A ponte pede `NO_PERSONALIZED_LEARNING`, `NO_EXTRACT_UI`, não salva conteúdo e mantém a proteção de tela remota `FLAG_SECURE`.

## Testes

- Verificações .NET de limpeza por sessão, preservação de originais/exportados/arquivos desconhecidos, permissões, validação de `edit` e revalidação de autorização durante consultas de foco.
- Teste nativo Windows em janela isolada: foco editável, identificação de senha sem leitura, entrada Unicode, substituição/apagamento ao vivo, emoji/backspace e exclusão de controles não editáveis.
- Testes Kotlin puros para diff, autocorreção, acentos, Unicode e divisão de lotes.
- Robolectric/API 35 exercita o `InputConnection` real do editor: commit sem Enviar, composição de acento, backspace vazio, troca de contexto, emoji e flags de proteção. Downloads/caches ficam no disco D.
- Integração OkHttp ↔ Kestrel usa arquivo sintético de **1.073.872.896 bytes** (> 1 GiB), gerado em blocos, upload e download completos, contagem de bytes e CRC32. Nenhum arquivo do usuário é usado nesse teste. As cópias grandes de teste são removidas ao encerrar o host isolado.
- Smoke do executável publicado semeia uma cópia interna e um arquivo externo: nova execução precisa apagar apenas a primeira e preservar autorização permanente através de reinício.

## Limitações a validar no telefone físico

Alguns aplicativos/elevados, terminais e controles customizados não expõem editabilidade, ou dão semânticas próprias ao backspace. O botão Teclado manual continua disponível. Composição, sugestões e abertura do teclado também dependem do IME Android instalado. O telefone não lê o texto preexistente, não sincroniza a seleção inteira do PC e não pode garantir entrega de teclas durante uma queda de rede. A entrada atua no foco/caret atuais do Windows.

Atualize Windows e APK (versionCode 5), instalando por cima sem desinstalar. A primeira abertura da versão 0.4 já limpa as cópias da biblioteca anterior, conforme solicitado. Pareamentos, identidade e autorização permanente são preservados. Tailscale e controle do Android pelo PC seguem adiados.
