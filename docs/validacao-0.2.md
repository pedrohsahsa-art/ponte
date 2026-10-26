# Validação da versão 0.2.0

## Concluído

- **78 verificações .NET** de pareamento, arquivos, HTTPS e autorização de controle. Incluem autorização separada por telefone, credencial de sessão, expiração, revogação, tipos de comandos, entrada inválida, interrupção local e falha fechada de desktop/injeção.
- **27 testes Kotlin/JVM**, incluindo pin TLS, leitura do QR/link, mapeamento de toque com barras de enquadramento e cliente OkHttp real contra o servidor HTTPS Windows.
- No teste de integração Kotlin/Windows, a tela é um **JPEG sintético** e os comandos chegam a um **adaptador simulado**. Não há captura da tela real nem digitação no desktop nesse teste de protocolo.
- Teste nativo separado: captura real do monitor principal em JPEG 1280 × 720, digitação Unicode, Backspace e clique absoluto em botão de uma janela WPF isolada. A imagem permaneceu em memória e não foi salva. Não foi alterado o volume nem foram usados documentos do usuário.
- Abertura e fechamento do executável publicado, geração de QR e painel de controle inicialmente desativado, verificados por UI Automation com dados de teste separados.
- Compilação Windows sem erros/avisos. Android Lint sem erros; permanecem avisos de versões mais recentes de dependências e sugestões de estilo KTX, sem supressão global de verificações.

As primeiras tentativas do teste nativo foram interrompidas antes da injeção porque a janela de teste não conseguiu foco. O teste foi ajustado para ativar somente a própria janela visível, conferir o foco antes de cada entrada e restaurar a janela/cursor anteriores. O teste subsequente passou.

## Preservação

- A biblioteca, a identidade HTTPS e os dispositivos pareados existentes não são migrados nem apagados.
- O APK mantém `applicationId=br.com.ponte`, passa de `versionCode=1` para `2` e usa a mesma chave local de assinatura, verificada pelo script de release.
- A atualização do APK deve ser instalada por cima da anterior, sem desinstalar.
- `dist\backups` preserva os artefatos substituídos. O APK ainda é uma compilação de desenvolvimento assinada localmente, não uma publicação na Play Store.

## Ainda não validado

- Nova interface de controle/gestos no **telefone físico do usuário**, incluindo orientação, teclado virtual e comportamento de cada aplicativo Windows.
- Volume do sistema: o mapeamento foi validado pelo protocolo/adaptador, mas o teste automatizado não altera o volume real.
- Bloqueio e desconexão reais da sessão Windows: a política de interrupção é coberta por testes com desktop simulado indisponível; o computador não foi bloqueado/desconectado automaticamente.
- Múltiplos monitores/drag com botão pressionado/áudio: não implementados.
- **Tailscale/acesso externo: testes expressamente adiados pelo usuário.**

Os testes automatizados de política, protocolo e desktop não substituem o teste final de usabilidade com o telefone.
