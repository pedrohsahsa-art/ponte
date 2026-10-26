# Hotfix 0.3.1 — conexão e carregamento sem diagnóstico

## Evidência encontrada no PC

Na inspeção inicial não havia processo Ponte.Windows nem servidor escutando a porta TCP 47831. O PC tinha `192.168.2.153` em Ethernet (rede local) e `26.96.90.226` em Radmin VPN. O algoritmo anterior só dava preferência ao Tailscale e, na ausência dele, podia selecionar o Radmin pela ordem de enumeração dos adaptadores. Não havia arquivo de autorização permanente nesta instalação; o arquivo `data/devices.json` continha uma lista vazia, sem pareamentos aceitos pelo servidor.

Esses fatos não revelam o IP salvo no telefone e não provam conectividade entre o telefone físico e o PC. Porém, o programa fechado impede a conexão, e o endereço de VPN não é o endereço a usar pela rede Wi-Fi local. A rede Ethernet estava classificada como pública, mas existiam regras de firewall permitindo o executável nesse perfil. Nenhuma regra de firewall, configuração de VPN ou roteador foi alterada neste hotfix.

## Correções

- Seleção automática prioriza rede local física com gateway, depois LAN física e só depois interfaces virtuais/VPN. VPNs continuam disponíveis para seleção manual.
- Windows informa o IP recomendado e registra na interface a última chamada de status, pareamento ou abertura/fechamento de sessão, com IP, caminho estático e código HTTP. Não registra query strings, tokens, códigos de pareamento ou conteúdo da tela.
- Android testa o status do PC antes de solicitar autorização e abrir vídeo. Exibe IP, porta, etapa e número da tentativa. A causa da falha não é substituída por uma mensagem genérica a cada repetição.
- Botão Parear novamente também aparece quando o Android já tem uma conexão salva. Permite refazer QR/link e substituir a credencial só após aprovação, sem apagar arquivos ou exigir desinstalação.
- Consultas de status/lista de arquivos usam limite de oito segundos; não compartilham o timeout longo das transferências. O pareamento mantém tempo próprio para a aprovação no PC.
- Abertura do stream tem watchdog de primeiro quadro em dez segundos e timeout de leitura durante o handshake. Isso não limita a duração de uma transmissão saudável. Erros continuam reconectando conforme a política existente, sem esconder falta de autorização ou falha de certificado.

## Testes e limitações

- Passaram 99 verificações .NET e a suíte Kotlin/JVM, incluindo os cenários de conexão travada. O executável 0.3.1 foi publicado e deixado aberto com os dados reais da instalação. O probe confirmou HTTPS pelo endereço `192.168.2.153:47831` com a identidade TLS exata; HTTP 401 sem token foi o resultado correto. A interface também confirmou esse IP como preferencial. Nenhum pareamento ou autorização real foi concedido automaticamente.
- Testes de seleção de endereço incluem exatamente o caso Radmin + Ethernet desta máquina, variantes virtuais, endereços inválidos/link-local e duplicatas.
- Testes JVM simulam um servidor que aceita TCP e nunca responde ao TLS: status e stream precisam falhar em prazo limitado, em vez de carregar indefinidamente.
- A integração real Kotlin/OkHttp ↔ Windows/Kestrel continua exercitando 240 quadros sintéticos Full HD e verifica a resposta de versão do status do PC.
- O probe local (`scripts/probe-pc.ps1 -Address IP`) verifica o servidor publicado pelo endereço LAN com pin TLS exato e sem credenciais; HTTP 401 é o resultado esperado. Ele não cria pareamento nem concede controle e não substitui o teste a partir do telefone.

Instale o APK de `versionCode=4`/`versionName=0.3.1` por cima do anterior, sem desinstalar. Abra a versão atual do programa Windows e mantenha-a aberta (pode minimizar). No Android, use o IP local atual mostrado no Windows; se já estiver pareado, basta Trocar endereço. Se faltar autorização de controle, ela deve ser confirmada uma vez no PC e continua permanente depois disso.
