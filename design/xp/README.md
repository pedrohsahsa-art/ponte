# Ponte · Clássico XP

Referência visual e ícones originais criados no Blender. Não contém imagens,
ícones ou fontes redistribuídas do Windows XP. Tahoma é usada da instalação
local somente como referência de renderização; o aplicativo usa a fonte do sistema.

Para gerar novamente, a partir da raiz:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\render-xp.ps1
```

Cada execução cria uma nova pasta `render-DATA` com `ponte-xp.blend` e
`ponte-xp.png`; os ícones são atualizados nas pastas de recursos dos dois apps.
O processo usa `--background --factory-startup`, sem modificar cenas abertas.

## Aplicação nativa

- Windows: barra Luna azul, painéis Explorer, botões com relevo e ações verdes.
- Android: cabeçalho azul, fundo creme, ícones 3D e painéis compactos.
- Três áreas: Controlar, Arquivos e Conectar/Conexão.
- Internet, endereços manuais e DDNS permanecem em opções avançadas.
- A aparência clássica não implica compatibilidade com Windows XP.
- Autorizações, avisos de captura, parada e revogação continuam explícitos.

O mockup é uma referência de composição, não uma captura do aplicativo.
