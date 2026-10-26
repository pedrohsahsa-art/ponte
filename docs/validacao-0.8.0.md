# Validação local · Ponte 0.8.0

## Mudança visual

Windows e Android receberam tema inspirado no XP Luna e ícones originais
renderizados no Blender. Interface nativa, sem incorporar o Blender ao runtime.
As telas principais são Controlar, Arquivos e Conectar/Conexão. Endereços,
internet e DuckDNS ficam recolhidos em opções avançadas.

Fontes do estudo e instruções: [design/xp](../design/xp/README.md).

## Verificado

- Compilação Windows Release sem erros ou avisos.
- 168 verificações .NET de autenticação, consentimento, autorizações persistentes,
  transferências, internet, DDNS e transporte de tela/controle.
- Janela WPF real com dados sintéticos: QR, navegação entre as três abas,
  internet desligada por padrão e restauração de preferências/grant.
- Fechar e reabrir mantém autorização permanente; limpeza fica limitada às
  cópias internas da biblioteca. Arquivos externos são preservados.
- Captura da janela de teste inspecionada em `.cache/ui/windows.png`.
- Clique e arrasto reais do Windows no visualizador com JPEG sintético chegam
  ao socket autenticado de teste, inclusive quando chega outro frame no arrasto.
- Android: lintDebug, assembleDebug e 79 testes em 14 suítes concluídos sem falhas.

## Limitações

Nenhum Android físico conectado via ADB nesta validação. Ainda é necessário
conferir o layout em diferentes tamanhos de tela/fontes, captura e gestos no
telefone e reconexão Wi-Fi/4G. Mockup Blender não substitui esse teste.

Nenhuma publicação da 0.8.0 no GitHub é realizada automaticamente.
Ao abrir o app Windows real, continua valendo a limpeza da biblioteca interna;
salve fora dela as cópias que quiser manter antes de reabrir.
