# Validação 0.3 — controle do PC pelo Android

## Alterações motivadas pelo uso real

- Retirado o polling de quatro quadros por segundo da interface Android. Agora há um WebSocket TLS contínuo, com um único JPEG em trânsito e ACK após decodificação, alvo máximo de 20 fps e resolução até 1920 × 1080.
- Timeout HTTP curto não é aplicado ao stream. Falhas transitórias passam a reconectar automaticamente, sem quantidade/duração máxima de tentativas. Erros de pin/certificado e pareamento revogado continuam bloqueados.
- `ON_PAUSE` não encerra mais a sessão. Segundo plano real pausa a recepção; voltar retoma automaticamente. A rotação é tratada sem recriar a atividade por mudanças de orientação/tamanho.
- Tela cheia na horizontal por padrão, controles sobrepostos/recolhíveis, Ajustar/Preencher, zoom até 6× e deslocamento. A transformação inversa dos cliques acompanha exatamente o zoom/pan da imagem.
- Autorização permanente persistida em `data/remote-permission.json`. Somente Parar/revogar, atalho de revogação ou revogação do telefone a retiram. Bloqueio, falhas de captura/entrada, caixas de confirmação e encerramento do Ponte não a apagam.
- Sessões de transporte continuam usando credenciais aleatórias, não persistidas e rotacionadas ao reconectar. Comandos não confirmados não são repetidos após falhas.

## Cobertura automatizada

- **90 verificações .NET**: pareamento, arquivos, autorização permanente/reinício, revogação persistente, pausa sem perder permissão, recuperação de entrada, validação de comandos e stream TLS. O teste de backpressure confirma que nenhum segundo quadro é enviado antes do ACK. Revogar fecha o stream sem enviar outro quadro. Falha de persistência não impede retirar o acesso em memória.
- Testes Kotlin/JVM de TLS/pin, pareamento, enquadramento com zoom/pan, classificação de erros para reconexão e backoff sem limite de tentativas.
- Integração Kotlin/OkHttp real com Windows/Kestrel: **240 JPEGs sintéticos Full HD** no mesmo WebSocket, atravessando o antigo timeout de oito segundos, conferidos byte a byte. Conectar novamente não exige outra autorização do PC. Não usa a tela real nesse teste.
- Teste nativo separado: captura real 1920 × 1080, Unicode, Backspace e clique em janela WPF isolada. Imagens não são gravadas nem enviadas a serviços externos. O volume real não é alterado pelo teste.
- Benchmark local de captura/encoder: 30 quadros medidos, mediana **27,9 ms**, média **28,4 ms**, JPEG médio **172 KiB** na execução inicial. Mede só captura/encoder do PC, **não** FPS de rede/telefone.
- Smoke test do executável publicado: instalação nova sem autorização e autorização explícita de teste carregada/salva através de fechamento e reinício reais do aplicativo.

## Atualização e limites

Atualize Windows e APK. O APK é `versionCode=3`, mesma identidade/chave de assinatura das versões anteriores. Instale por cima, sem desinstalar; arquivos, certificado e pareamentos são mantidos.

É necessário autorizar o controle **uma vez no Windows 0.3**, pois 0.2 só guardava esse consentimento em memória. A partir daí ele fica salvo, sem vencimento. Nenhuma autorização é criada silenciosamente a partir do simples pareamento de arquivos.

Ainda é necessário validar FPS, gestos, teclado e retorno de segundo plano no **telefone físico do usuário**, na mesma rede Wi-Fi. O contador mostra o FPS real. O alvo de 20 fps não é garantia para qualquer telefone/rede nem equivalente a WebRTC/H.264/60 fps.

Tela bloqueada/UAC continuam protegidos pelo Windows; não há bypass. O PC precisa estar ligado e o Ponte aberto, mas pode ser fechado/reaberto sem perder a autorização. Tailscale e controle do Android pelo PC permanecem adiados a pedido do usuário.
