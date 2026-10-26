# Controle PC → Android

## Fluxo e limites de consentimento

O telefone inicia um WebSocket autenticado para o servidor já pareado do PC. Não há servidor HTTP no Android, porta nova, root ou ADB. O Windows abre um visualizador para o telefone selecionado; o telefone precisa iniciar o compartilhamento e aceitar o diálogo de captura do Android. O controle por gestos exige também ativação manual do Serviço de Acessibilidade do Ponte.

A captura deve permanecer visível em notificação de primeiro plano, com ação Parar. Fechar o visualizador, perder o transporte, bloquear o telefone ou encerrar a captura deve retirar a autorização de entrada e parar o compartilhamento. Não se deve reutilizar silenciosamente uma autorização MediaProjection após encerramento. Conteúdo protegido não é contornado. Acessibilidade ativada, sozinha, não autoriza comandos sem sessão explícita de compartilhamento.

## Transporte implementado (primeira versão)

- `/api/android/stream`: bearer do dispositivo, origem sujeita à mesma política de internet do Ponte. Admite somente o telefone cujo visualizador local foi aberto.
- Telefone envia JPEG limitado, no máximo um em voo. PC responde ACK após validar/decodificar. Sem gravação de tela em disco.
- PC envia comandos JSON limitados: tap, swipe, back, home, recents e substituição explícita de texto do campo editável focado. Sem shell, teclas arbitrárias, permissões automáticas ou leitura de senhas.
- Região de imagem mapeada exclui letterboxing. Para manter coordenadas corretas, a captura inicial será de display inteiro, não janela selecionada. Mudança de tamanho/orientação encerra a sessão e exige novo compartilhamento.
- Configurações do sistema, diálogos de permissão e o próprio Ponte não devem ser comandados remotamente.

## Validação necessária

Além de testes de protocolo/limites, MediaProjection, gestos Accessibility, notificações e comportamento com tela protegida exigem um telefone físico e consentimento no dispositivo. Um teste sintético no servidor não prova esse comportamento Android.

## Como usar

1. Atualize o APK por cima da instalação existente.
2. No Windows, abra **Dispositivos**, selecione o telefone e clique em **Abrir Android**.
3. No Android, abra **Dispositivos → Compartilhar Android**, leia o aviso e autorize notificações e captura no diálogo do sistema.
4. Para controlar, abra **Permitir controle** e habilite manualmente o serviço Ponte na Acessibilidade. Se o Android restringir configurações de aplicativos instalados por APK, siga apenas as opções oficiais do próprio sistema; o Ponte não habilita permissões sozinho.
5. Saia da tela do Ponte para o aplicativo que quiser controlar (a partir da versão 0.7.1, Início/Voltar no visualizador podem fazer essa navegação). Clique na imagem no Windows para tocar; arraste para deslizar. Botão direito volta; há botões Início e Recentes. Aplicar texto substitui o conteúdo do campo editável focado (não é espelhamento de teclado em tempo real e não atua em senhas). O rodapé do visualizador informa as recusas e o estado de Acessibilidade do telefone.
6. Pare pela notificação Android, pelo aplicativo ou fechando o visualizador Windows. Mudança de rede, rotação ou bloqueio exige nova captura.

Não há áudio do Android nesta primeira versão. O protocolo e a compilação foram exercitados, mas a captura e os gestos ainda precisam ser validados no telefone físico do usuário. A filtragem de entrada verifica o pacote da janela ativa imediatamente antes da ação; não é uma garantia contra toda transição de janela que possa ocorrer simultaneamente.
