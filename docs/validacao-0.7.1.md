# 0.7.1 — diagnóstico e correção do controle Android

## Sintoma

Usuário confirmou que a tela chegava ao PC, mas entrada continuava sem efeito depois de habilitar o controle no Android. Sem conexão física ao telefone, não é possível atribuir a causa observada a um único ramo. Foram identificadas e corrigidas falhas reproduzíveis no código que podiam produzir esse mesmo sintoma.

## Correções

- `onInterrupt` interrompe feedback da Acessibilidade; não revoga mais, de forma indevida e permanente, uma autorização MediaProjection ativa. Encerrar captura, bloquear o telefone, fechar o transporte e parar continuam removendo a autorização.
- Início/Voltar não precisam da árvore de janela e podem sair do próprio Ponte ou de uma tela de configurações. Isso não permite aprovar permissões. Tap/swipe/text continuam bloqueados no Ponte, configurações, System UI e controladores de permissão/instalação.
- Acessibilidade solicita janelas interativas e usa a janela ativa/focada como alternativa quando a raiz principal não estiver disponível.
- Serviço desconectado e ausência da capacidade de gestos são detectados explicitamente. Resultado do Android (gesto enviado/concluído/cancelado, campo ausente/protegido etc.) é informado ao PC, sem transmitir texto de campos, senhas, pacote ativo ou conteúdo de eventos.
- Feedback usa mensagens texto JSON limitadas a 1024 bytes no socket autenticado; códigos desconhecidos são rejeitados. JPEG/ACK e suas limitações permanecem separados.
- Status do visualizador ocupa altura estável para não deslocar as coordenadas da imagem quando o texto do diagnóstico muda. Envio local recusado não é anunciado como sucesso.

## Evidência

- `scripts/test-viewer-input.ps1`: janela WPF real, imagem sintética e mouse nativo restrito à janela de teste. Confirma clique → comando tap no WebSocket autenticado e atualização da imagem entre mouse-down/up sem perder o arrasto → swipe.
- Robolectric exercita interrupção sem revogação de captura, navegação sem raiz, bloqueio de cliques no próprio Ponte, exigência de autorização, ausência de janela e ausência de capacidade de gestos.
- Integração .NET testa diagnóstico de Acessibilidade no mesmo socket antes do JPEG, sem transformá-lo em ACK, seguido de imagem, entrada e interrupção da sessão.

## Teste no aparelho

1. Atualize o APK por cima; Windows e Android precisam estar na versão 0.7.1.
2. No telefone, desligue e ligue novamente **Ponte · controle pelo PC** na Acessibilidade para recarregar a configuração do serviço. Se o Android mostrar restrições, use somente as opções oficiais do sistema, manualmente.
3. Abra o visualizador no PC, inicie uma nova captura no telefone e use **Início** no visualizador para sair da tela do Ponte. Teste em um aplicativo comum, não na tela de permissões.
4. Se não funcionar, informe a mensagem completa no rodapé do visualizador e o modelo/versão Android. O texto diferencia serviço ausente, janela protegida, janela indisponível e gesto recusado.

A confirmação no telefone físico do usuário continua necessária. Não houve ativação automática de Acessibilidade nem contorno de permissões.
