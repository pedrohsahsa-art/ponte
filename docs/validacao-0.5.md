# Ponte 0.5 — controle ampliado e visual renovado

Android versionCode 6, mesma identidade de assinatura. Windows e Android devem ser atualizados juntos: metadados de monitor e opções são parte do protocolo 0.5.

## Funcionalidades

1. Arrasto explícito em modo próprio; down/move/up ordenados, heartbeat 400 ms e liberação após 2 segundos sem renovação, mudança de monitor, falha, fechamento ou revogação.
2. Qualidade automática por FPS recebido/decodificado, com janelas de 4 s e histerese para subir. Perfis fixos também disponíveis.
3. Saída de áudio WASAPI, sem microfone, permissão local separada por execução e reprodução Android opcional. PCM16 estéreo em WebSocket TLS independente, filas limitadas e cancelamento de receptor lento.
4. Enumeração e seleção de monitores com captura, mouse virtual e teclado por área física; metadados antes de cada JPEG evitam aplicar cliques em quadros da tela anterior.
5. Descoberta LAN após falha e por botão; nonce e pin no anúncio, confirmação HTTPS com identidade já pareada antes de salvar endereço.
6. Wake-on-LAN explícito, com MAC/broadcast obtidos por endpoint autenticado e salvos no armazenamento cifrado. Não altera BIOS, energia, adaptadores ou roteador.

## Visual

- Identidade verde-petróleo, superfícies claras, cantos arredondados e hierarquia de títulos compartilhada entre Windows e Android.
- Android: cartão de entrada no controle, ajustes de conexão recolhíveis, biblioteca com contagem, cartões de arquivo e estado vazio ilustrado.
- Remoto: barra escura flutuante, botões com indicação de estado, painéis de enquadramento/teclado/mouse/monitor e recolhimento para liberar a imagem.
- Windows: cartões com bordas suaves, botões arredondados com estados de foco/hover/desativado, ações de revogação em vermelho e aviso de biblioteca temporária preservado.

## Evidência e limitações

- Testes .NET cobrem autorização, metadados, seleção/perfis, áudio bloqueado sem permissão, nonce/pin da descoberta e liberação de arrasto.
- Testes nativos usam janela isolada, sem documentos do usuário. Neste PC foram encontrados dois monitores (um à esquerda, coordenadas negativas). Captura dos três perfis nos dois monitores, mouse down/move/up e clique/teclado no monitor secundário foram exercitados.
- `scripts/test-audio.ps1`: WASAPI real com reprodução de silêncio, socket TLS autenticado, formato e bytes PCM, encerramento por retirada da permissão. Nenhum microfone é aberto e nenhum áudio é gravado em disco.
- Kotlin testa pacote mágico, preservação dos dados WoL e histerese. Integração OkHttp/Kestrel testa metadados reais do stream, opções, arrasto e transferência sintética >1 GiB.
- `scripts/smoke-windows.ps1 -WithGrant -Screenshot` verifica aplicação publicada, limpeza por sessão e autorização persistente, com captura visual somente de dados de teste isolados em `.cache/ui/windows.png`.
- Não há telefone Android conectado via ADB. A reprodução no alto-falante do telefone, abertura do IME real, gestos, visual em tamanhos/fontes específicos, mudança real de IP entre aparelhos, broadcast Wi-Fi e despertar físico do PC ainda precisam de validação no dispositivo/rede. O envio de magic packet não é confirmação de despertar. Não foi colocado o PC do usuário em suspensão.
- Sem WebRTC/H.264 e sem sincronização audiovisual exata. PCM usa cerca de 1,5 Mbit/s a 48 kHz estéreo. Mudança do dispositivo padrão de áudio pode exigir desligar e ligar Ouvir PC.

As regras de firewall já existentes não são removidas nem substituídas. O script de configuração adiciona, quando executado como administrador, descoberta UDP 47832 somente para sub-rede local no perfil Privado. IP manual continua sendo alternativa.
