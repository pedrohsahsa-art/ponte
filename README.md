# Ponte

Aplicativo pessoal para conectar **Android e Windows**, com o projeto e as ferramentas em `D:\programations\ponte`.

**Versão local: 0.8.0 — interface Clássico XP.** Windows e Android reorganizados em Controlar, Arquivos e Conectar/Conexão, com ícones originais feitos no Blender. [Referência visual e arquivo editável](design/xp/README.md). A release pública continua em 0.7.1 até nova publicação autorizada.

### Novidades

- **DuckDNS integrado:** atualização automática de AAAA ao iniciar, a cada 5 minutos e após mudanças de endereço. Subdomínio/token configurados no Windows; token protegido com DPAPI e nunca exibido novamente. É necessário criar sua conta gratuita no DuckDNS. [Configuração e teste de reconexão](docs/ddns-reconexao.md).
- **Reconexão por mudança de rede:** Android observa a rede padrão, reinicia o controle do PC sem repetir comandos antigos e preserva a intenção de áudio após quedas de transporte. Testes físicos Wi-Fi/4G e bloqueio/desbloqueio continuam necessários.
- **Android no PC:** abra o telefone na aba Dispositivos do Windows e inicie Compartilhar Android no telefone. MediaProjection com notificação Parar; gestos e texto simples via Acessibilidade opcional. Sem root, ADB ou porta no telefone. [Fluxo, limitações e consentimento](docs/pc-android.md).
- **Clássico XP:** barra azul, fundo creme, painéis Explorer e ícones 3D próprios. Três tarefas principais; endereços, internet e DDNS ficam em opções avançadas. Permissão/parada continuam explícitas.

### Compartilhar uma build de teste

Downloads públicos: [ponte-releases](https://github.com/pedrohsahsa-art/ponte-releases/releases).
APK Android e ZIP Windows portável são distribuídos sem dados, pareamentos ou chaves privadas.
No outro PC, extraia o ZIP completo e execute **Iniciar Ponte.cmd**. A pasta de dados é criada
ao lado do iniciador, sem depender do disco/caminho do desenvolvedor.
[Empacotamento e publicação](docs/distribuicao.md).

### Fora de casa

O Ponte guarda endereço local e de internet separadamente e aceita IPv6/domínio DDNS com o mesmo pin TLS. No Windows, ative **Permitir dispositivos já pareados pela internet**; ainda no Wi-Fi, use **Aprender IPv6 do PC** no telefone para aprender o IPv6 ou hostname DDNS confirmado. Depois teste no 4G/5G. O roteador/firewall pode precisar de uma regra restrita TCP 47831 para o PC. O Ponte **não abre portas automaticamente** nem cria contas DDNS. Pareie primeiro na LAN; pareamento público é bloqueado.

Instruções e limites: [Internet direta sem mensalidade](docs/internet-direta.md).

### Novos controles

- **Arrastar:** ative o modo na barra da tela remota, deslize com um dedo e solte para largar. Serve para janelas, seleção de texto e arquivos dentro do Windows, não para enviar arquivos entre apps. Desative para voltar a clicar/pinçar. O botão é liberado ao desconectar, revogar, mudar de monitor ou perder o heartbeat por 2 segundos.
- **Monitor/qualidade:** escolha um monitor e o perfil Automática, Fluidez, Equilibrada ou Nitidez. A seleção afeta captura, clique e detecção de teclado. Automática considera FPS recebido/decodificado em janelas de 4 segundos e só aumenta após três janelas boas, sem testar velocidade ou consumir transferências extras.
- **Ouvir PC:** primeiro marque **Permitir áudio de saída do PC** no Windows; depois toque em Ouvir PC no Android. Captura somente a saída padrão via WASAPI, nunca o microfone. O áudio usa outro WebSocket TLS, PCM 16-bit estéreo, com fila limitada; pode ser desligado sem parar a tela. Não há garantia de sincronização audiovisual exata. Permissão de áudio não é persistida entre execuções.
- **IP alterado:** após falhas de rede, o telefone procura o PC na LAN. Anúncios UDP são apenas pistas: a identidade TLS salva e a autenticação precisam ser verificadas antes de salvar o endereço. **Localizar PC na rede** permite procurar manualmente; trocar o IPv4 continua disponível. Redes de convidados/VPNs ou isolamento Wi-Fi podem bloquear a descoberta.
- **Acordar PC:** conecte uma vez ao PC ligado para salvar MAC/broadcast pelo HTTPS autenticado. Depois use **Acordar PC · Wake-on-LAN**, na tela inicial. O envio do pacote não confirma que o PC acordou. BIOS/UEFI, placa, energia e rede precisam suportar e permitir WoL; Ethernet costuma ser mais confiável. Não altera configurações de energia nem configura o roteador. Com o Ponte fechado, acordar o Windows não inicia o app automaticamente.

Descoberta utiliza **UDP 47832** na rede local; HTTPS/tela/áudio continuam em **TCP 47831**. Se necessário, execute `scripts/firewall.ps1` como administrador: a regra de descoberta fica limitada à sub-rede local no perfil Privado. Não é necessário abrir portas no roteador.

### Mudanças da versão 0.4

- **Ao reabrir o Ponte no PC, as cópias da biblioteca anterior são apagadas**, conforme a preferência escolhida pelo usuário. Originais e cópias salvas fora da biblioteca não são alterados. Quedas de Wi-Fi, fechar a tela remota e reconectar o telefone não limpam os arquivos.
- A primeira abertura desta atualização também limpa as cópias internas já existentes. Antes de reabrir, use **Salvar cópia…/Baixar** se quiser preservar algo que só esteja na biblioteca.
- **Sem teto de 1 GB do aplicativo**, nos dois sentidos. Usa streaming e tamanhos de 64 bits; os limites reais continuam sendo espaço livre, permissões/provedor e sistema de arquivos dos dispositivos. O timeout absoluto de 30 minutos de transferência foi removido, mas falta de atividade/rede continua gerando erro.
- A lista do Android acompanha a sessão do PC e atualiza automaticamente enquanto a tela de arquivos estiver aberta. Pareamentos e autorização permanente não são apagados na limpeza.
- Ao clicar pelo telefone em um campo editável que exponha acessibilidade, o teclado Android abre automaticamente. Cada inserção, correção e apagamento é enviada na hora, **sem Enviar**. **Teclado** no menu continua disponível manualmente para aplicativos incompatíveis.
- A detecção usa só metadados de foco/editabilidade/senha. Não lê o valor dos campos no PC. O telefone mantém somente um contexto curto da própria digitação para IME/acentos/correções, sem salvar ou registrar esse texto. Ao trocar de campo, esconder o teclado ou desconectar, o contexto é descartado.

### Se ficar conectando sem chegar à tela

- Abra `Abrir Ponte.cmd` e **mantenha o programa aberto no PC**; minimizar pode, fechar encerra o servidor.
- No Windows, o endereço recomendado passa a priorizar a rede local física, não adaptadores VPN. Nesta máquina foi identificado `192.168.2.153` em Ethernet; `26.96.90.226` pertence ao Radmin VPN, não à rede Wi-Fi local. Endereços podem mudar com o DHCP; confira o endereço atual na janela.
- No Android já pareado, informe o IPv4 local no campo de conexão e use **Trocar endereço**. Isso verifica a identidade do mesmo PC e não exige apagar o pareamento.
- Se aparecer que o PC respondeu mas falta autorização, selecione seu telefone no Windows e use **Autorizar telefone selecionado** uma vez. A autorização fica salva, sem vencimento.
- Se a lista do Windows estiver vazia ou o pareamento estiver inválido, use **Parear novamente** no Android e escaneie um novo QR do PC. Não é necessário desinstalar nem apagar a biblioteca; a credencial anterior só é substituída após aprovação do novo pareamento.
- A versão 0.3.1 mostra IP, porta, etapa, tentativa e a causa da falha. O erro não é mais apagado por uma mensagem genérica de reconexão. A janela Windows mostra a última requisição de status/pareamento/sessão recebida, sem tokens ou códigos.
- Consultas pequenas de status/arquivos têm limite de oito segundos, separado do timeout de arquivos grandes. Se o primeiro quadro não chegar em dez segundos, o Android informa a falha e tenta novamente; esse limite não encerra transmissões saudáveis.

## O que esta versão faz

- Pareamento por QR Code ou link, válido por cinco minutos e de uso único.
- Aprovação explícita no Windows antes de autorizar um telefone.
- Transferência de arquivos nos dois sentidos, **sem limite fixo de tamanho do app**.
- Biblioteca isolada: nenhum acesso ao restante dos arquivos do computador.
- HTTPS com identidade do computador verificada pelo Android a partir do QR Code.
- Revogação de telefones no Windows.
- Transmissão contínua do monitor selecionado, com cursor, até 1920 × 1080 e limite de envio de 20 quadros/s conforme perfil/PC/rede/telefone.
- Tela cheia na horizontal, zoom por pinça até 6×, deslocamento da imagem e opções Ajustar/Preencher.
- Reconexão automática após falhas temporárias, sem exigir outro toque em Conectar.
- Clique, duplo clique, botão direito, movimento do mouse e rolagem.
- Digitação Unicode em tempo real, teclado automático em campos compatíveis, teclas especiais, atalhos comuns e volume do Windows.
- Autorização separada de tela/controle, restrita a um telefone por vez, **sem vencimento e preservada ao reiniciar o PC/Ponte**. Só é retirada por revogação manual.
- Botão de parada no PC e atalho global **Ctrl+Alt+F10**, quando disponível.
- Conexão por Wi-Fi e por IP Tailscale, sem servidor próprio e sem abrir portas no roteador.

**Não implementado nesta versão:** áudio do Android no PC, teclado PC → Android em tempo real, sincronização automática de arquivos e execução Windows como serviço/bandeja/início automático. A captura Android tem um serviço de primeiro plano próprio e notificação de parada; isso não inicia transferências automáticas em segundo plano. Fechar a janela do Windows encerra o servidor.

## Atualizar

1. Abra o programa Windows atualizado pelo mesmo `Abrir Ponte.cmd`.
2. Instale `dist\android\ponte.apk` **por cima do aplicativo existente**, sem desinstalar. O identificador e a chave local de assinatura foram mantidos.
3. Os arquivos, a identidade HTTPS e os pareamentos existentes são preservados. Não é necessário novo QR Code para esta atualização.
4. Se você já autorizou a versão 0.3/0.3.1, o acesso continua salvo; não é necessário reautorizar. Somente quem vem da versão 0.2 precisa autorizar uma vez, pois ela guardava o consentimento apenas em memória.
5. Lembre-se: a nova sessão do Windows limpa as cópias internas dos arquivos. Isso não limpa pareamentos, certificados ou autorização de controle.

Os scripts preservam os executáveis/APK anteriores em `dist\backups`, sem tocar em `data`. O Windows costuma permitir atualizações do APK, mas não downgrade para um `versionCode` menor; o backup anterior não é um instalador de downgrade automático.

## Executar

1. No PC, abra **`Abrir Ponte.cmd`** ou `dist\windows\Ponte.Windows.exe`.
2. Transfira **`dist\android\ponte.apk`** ao telefone e instale. O Android poderá pedir autorização para instalar aplicativos dessa origem. O APK desta primeira versão é uma compilação de desenvolvimento assinada com chave de depuração local; não é uma publicação na Play Store.
3. Com o telefone e o computador no mesmo Wi-Fi, escolha no Windows o IPv4 do Wi-Fi e clique em **Gerar QR Code**.
4. No Android, toque em **Escanear QR Code**. O leitor usa o Google Play Services e pode baixar seu módulo na primeira utilização. Se indisponível, use o campo de link de pareamento.
5. Confira e aprove o pedido no Windows. O pedido expira em 60 segundos; gere outro QR Code se necessário.
6. No Windows, use **Adicionar do PC** para disponibilizar arquivos. No Android, use **Atualizar** e **Baixar no telefone**.
7. No Android, use **Enviar ao PC**. No Windows, selecione o arquivo e clique em **Salvar cópia…** para exportá-lo da biblioteca.

Os arquivos adicionados à biblioteca são **copiados**, não movidos. Remover ou editar o original não altera a cópia compartilhada. As cópias são temporárias e apagadas ao reabrir o programa Windows. Use **Salvar cópia…** ou **Baixar** para manter arquivos fora da biblioteca antes de começar outra sessão.

### Ver e controlar o PC pelo Android

1. No Windows, selecione seu telefone em **Telefones autorizados**.
2. Clique em **Autorizar telefone selecionado**, no painel **Tela e controle remoto**, e confirme **fisicamente no PC**. O painel fica destacado enquanto a permissão estiver habilitada.
3. No Android, abra **Tela e controle do PC**. A conexão inicia automaticamente, na horizontal e em tela cheia. O botão **Girar** permite mudar para vertical; a orientação anterior é restaurada ao sair.
4. Na imagem, **toque** para clicar, **toque duas vezes** para duplo clique e **segure** para clique direito. Em zoom 1×, deslizar move o cursor; com zoom ampliado, deslizar desloca a imagem. Use **pinça** ou **+/−** para ampliar até 6×. **Ajustar** mostra o monitor inteiro sem distorção; **Preencher** ocupa a tela, recortando as bordas que não couberem. Você pode deslocar a imagem ampliada para chegar a essas bordas.
5. Toque em um campo editável no PC: o teclado Android abre automaticamente quando o programa permite detectar o campo. Digite ou apague e veja a alteração em tempo real, sem Enviar. Caso o campo não seja detectado, abra **Teclado** no menu. Há Enter, Backspace, setas, Tab, Esc, Alt+Tab e Ctrl+A/C/V/Z. **Volume/rolagem** mostra os demais controles. O menu recolhe sozinho; toque em **☰** para reabri-lo. O contador mostra os FPS reais do telefone.
6. Para retirar a autorização, clique em **Parar/revogar** no Windows ou pressione **Ctrl+Alt+F10**. O atalho pode estar reservado por outro programa; o botão continua funcionando. **Desconectar** na tela de controle do Android apenas encerra a conexão atual, não apaga o pareamento nem a autorização permanente.

**Autorizar controle permite ao telefone ver informações na tela e interagir com programas.** Autorize apenas seu próprio telefone. O pareamento de arquivos não concede essa permissão automaticamente. Uma nova confirmação de autorização ou um novo pedido de pareamento interrompem o controle existente antes de mostrar a caixa de aprovação, para que o telefone não possa usar o próprio mouse remoto para aprová-la.

Detalhes desta etapa:

- Captura o **monitor selecionado**, até **1920 × 1080**, em JPEG por **WebSocket sobre TLS**, com alvo máximo de **20 quadros/s**. O telefone confirma cada quadro após decodificá-lo; o PC só então captura o próximo. Assim não se acumula uma fila de imagens antigas. A fluidez real depende do PC, Wi-Fi e telefone, e aparece no contador de FPS. Não é WebRTC/H.264 nem promessa de 60 fps para jogos ou vídeo.
- Cada conexão usa uma credencial de sessão nova, além do token de pareamento. Comandos de sessões antigas não são reaplicados ao reconectar, e comandos de entrada não têm repetição automática em falhas de rede.
- **Não há limite de duração da autorização ou da sessão por tempo ocioso.** A autorização do telefone fica salva no PC até você usar Parar/revogar ou revogar o telefone. Fechar o Ponte para o servidor, mas não apaga essa autorização; reabri-lo restaura o acesso. O programa ainda precisa estar aberto e o computador ligado/acordado para funcionar.
- Falhas temporárias reconectam automaticamente, com espera crescente entre 0,5 e 5 segundos, sem limite de tentativas. Bloqueio do Windows, mudança de área de trabalho ou falha de entrada **pausam**, sem revogar a autorização. Não há acesso à tela bloqueada/segura. Certificado incorreto ou pareamento revogado não são ignorados pelo mecanismo de reconexão.
- Voltar aos arquivos ou usar **Desconectar** encerra a conexão voluntariamente. Colocar o aplicativo em segundo plano pausa a recepção e limpa a imagem; voltar à tela retoma automaticamente se você não tiver desconectado voluntariamente. Perder foco por um instante ou girar o telefone não deve encerrar a sessão.
- O Android mantém a tela acordada durante a sessão. Capturas/gravações e a miniatura de aplicativos recentes são protegidas por `FLAG_SECURE`. Os controles ficam sobre a imagem, em vez de encolhê-la em uma coluna.
- As imagens não são gravadas em disco pelo Ponte. Um quadro já transmitido não pode ser recolhido depois de interromper a sessão.
- A execução continua como usuário normal. Não há controle da tela bloqueada, Ctrl+Alt+Del, UAC/área de trabalho segura ou garantia de controle de programas como administrador. Uma falha de comando nessa janela não retira a autorização nem deve derrubar a transmissão.
- A digitação é enviada em lotes pequenos e ordenados (até 500 caracteres por requisição, não por campo). Colagens maiores são divididas sem partir pares Unicode. Enter, tabulações, composição de acentos, correções do IME e backspace com contexto local vazio são tratados. Aplicativos elevados/protegidos, campos customizados, terminais e alguns navegadores podem limitar detecção ou ter semânticas próprias de edição; use a opção manual quando necessário.
- Não há leitura/sincronização do texto já existente no campo do PC. A entrada atua no caret/seleção/foco atuais do Windows. Se outra pessoa mudar o foco no PC, ela muda o destino da digitação. Em falha de rede, lotes não confirmados não são repetidos cegamente.
- Copiar/colar via Ctrl+C/V opera o clipboard do **PC**, não sincroniza a área de transferência do telefone. A ponte de teclado pede ao IME que não personalize/aprenda com a digitação e não use editor extraído em tela cheia; o comportamento final também depende do teclado Android instalado.

### Fora de casa com Tailscale

1. Instale o cliente oficial do [Tailscale no Windows](https://tailscale.com/download/windows) e no [Android](https://tailscale.com/download/android).
2. Entre na mesma conta nos dois dispositivos e conecte-os à sua rede Tailscale. Não compartilhe senhas com o aplicativo Ponte.
3. Identifique o IPv4 Tailscale do PC, normalmente iniciado por `100.` (faixa `100.64.0.0/10`).
4. No Ponte Android já pareado, substitua o endereço do Wi-Fi pelo IP Tailscale e toque em **Trocar endereço**. A identidade HTTPS do PC permanece a mesma; não é necessário novo pareamento.
5. O PC precisa estar ligado, acordado, com a janela Ponte aberta e o Tailscale conectado. O telefone também deve manter o Tailscale conectado.

É possível parear diretamente com o endereço Tailscale se os dois dispositivos já estiverem conectados a ele. A conexão não muda automaticamente entre endereços nesta versão. O acesso externo real depende das regras da sua rede Tailscale e só pode ser validado com o telefone conectado.

### Firewall

Se o Windows solicitar autorização de rede, permita apenas nas redes em que você pretende usar o aplicativo. Não desative o firewall nem configure encaminhamento de portas no roteador.

Se a conexão continuar bloqueada, existe uma regra opcional limitada ao executável, à porta TCP **47831**, à sub-rede local e à faixa de endereços Tailscale. Execute **somente se necessário**, em PowerShell como administrador:

```powershell
powershell -ExecutionPolicy Bypass -File D:\programations\ponte\scripts\firewall.ps1
```

O script não é executado automaticamente. A confiança do Tailscale é complementada pelo pareamento do Ponte, não o substitui. Em redes com isolamento de clientes Wi-Fi, a conexão local pode não funcionar; use o Tailscale ou ajuste a rede.

## Compilar

Os scripts usam ferramentas portáteis e caches sob esta pasta. Não exigem instalar Android Studio, Java ou .NET globalmente. Windows e ferramentas podem manter seus próprios metadados do sistema; isso não muda o local do projeto, dos downloads e dos artefatos.

```powershell
# .NET portátil
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1

# Java, Gradle e Android SDK; apresenta as licenças para leitura e aceite
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1 -Android

# Compilar e testar
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\build-android.ps1

# Opcional: abre e fecha uma janela de teste, com dados separados dos seus
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-windows.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-windows.ps1 -WithGrant

# Opcional: cliente Kotlin real contra servidor Windows, sem emulador/telefone
powershell -ExecutionPolicy Bypass -File .\scripts\test-android-protocol.ps1

# Opcional: teste nativo em janela isolada; requer área de trabalho desbloqueada
# e foco na janela de teste. Não altera o volume nem usa documentos do usuário.
powershell -ExecutionPolicy Bypass -File .\scripts\test-native.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\benchmark-capture.ps1
```

Requisitos do telefone: **Android 8.0 ou mais recente**. A câmera não é uma permissão permanente do Ponte; o leitor é fornecido pelo Google Play Services. O seletor de arquivos do Android concede acesso somente aos arquivos escolhidos, sem permissão de acesso total ao armazenamento.

## Estrutura

```text
windows/       Interface WPF e servidor HTTPS ASP.NET Core (.NET 10)
android/       Aplicativo Kotlin + Jetpack Compose
shared/        Pareamento, credenciais e biblioteca de arquivos
tests/         Testes executáveis de segurança e integração HTTPS
scripts/       Preparação, compilação e testes
tools/         SDKs portáteis (não versionar)
.cache/        NuGet, Gradle, temporários e testes (não versionar)
.downloads/    Downloads das ferramentas (não versionar)
data/          Identidade, telefones autorizados e biblioteca (não versionar)
dist/          Programa Windows e APK Android (não versionar)
```

## Segurança e dados

- O QR Code contém uma credencial temporária. **Não publique nem envie capturas do QR Code.**
- O token permanente só é entregue após a aprovação no PC. No PC é armazenado apenas seu SHA-256; no Android é armazenado cifrado com chave no Android Keystore.
- A identidade TLS do Windows é protegida por DPAPI para o usuário atual do Windows; não é instalado um certificado raiz confiável para o aplicativo.
- O certificado é verificado pelo SHA-256 exato, tem prazo de validade e não é substituído por um modo de confiança irrestrita. Se a identidade mudar, faça novo pareamento.
- Os downloads não seguem redirecionamentos e o token nunca é colocado na URL.
- Arquivos são armazenados com nomes internos aleatórios; nomes de arquivos recebidos não são usados como caminhos.
- Uploads interrompidos não entram na biblioteca e seus temporários são removidos quando a falha é detectada. Uma queda abrupta do processo pode deixar arquivos `.partial` sem publicação.
- Os arquivos compartilhados ficam sem criptografia adicional em `data\files`; proteja sua conta Windows e seu disco. Outros programas executados sob sua conta Windows podem ter acesso a eles.
- Revogar um telefone impede novas requisições; uma transferência já autorizada e em andamento pode terminar. Arquivos já baixados não podem ser apagados remotamente.
- **Esquecer PC**, na tela inicial do Android, apaga a conexão local/credencial do telefone. **Desconectar**, na tela de controle, só encerra a sessão atual. Nenhum dos dois revoga a autorização permanente no Windows; para isso, use Parar/revogar ou revogue o telefone no PC.
- Dados do programa Windows ficam por padrão em `D:\programations\ponte\data`. A variável `PONTE_DATA_DIR` permite outro diretório, se você decidir mover o projeto.
- Não envie `data`, caches, chaves ou ferramentas ao Git. Apagar `data` perde os pareamentos e a biblioteca. Mover a identidade para outro usuário Windows exige novo pareamento por causa da proteção DPAPI.

## Próximas etapas

1. Configurar conta DuckDNS do usuário e confirmar propagação AAAA sem compartilhar token no chat.
2. Executar a matriz física Wi-Fi → 4G/5G → Wi-Fi e bloqueio/desbloqueio, com tela/áudio/entrada.
3. Validar MediaProjection, Acessibilidade, parada e proteção de tela no telefone real; ajustar a primeira versão PC → Android conforme o aparelho.
4. Revisar ergonomia da UI minimalista em diferentes tamanhos de tela, fontes e orientações.
