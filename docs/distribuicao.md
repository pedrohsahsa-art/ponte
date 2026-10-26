# Distribuicao de builds para teste

Repositorio publico de downloads: https://github.com/pedrohsahsa-art/ponte-releases

O repositorio contem instrucoes e releases, nao uma copia da pasta de trabalho.
Nao envie `data`, `.cache`, `.downloads`, chaves, keystores ou tokens ao GitHub.

## Gerar pacotes

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-share.ps1
```

O script compila/testa o APK e publica o Windows em um diretorio de staging novo,
sem fechar o Ponte que estiver em uso. A saida fica em `dist/share/ponte-VERSAO-DATA`:

- APK assinado com a identidade de desenvolvimento existente;
- ZIP Windows x64 autocontido, sem PDBs, dados, certificados ou pareamentos;
- instrucoes, notas e hashes SHA-256.

O ZIP usa **Iniciar Ponte.cmd**, que define `PONTE_DATA_DIR` para a pasta `data`
do proprio pacote extraido. Isso permite testar em outro PC/drive sem usar o caminho
padrao do desenvolvedor. Execute o iniciador, nao o EXE interno isoladamente.

A verificacao do ZIP permite somente os binarios e os dois arquivos de configuracao
do runtime vindos do publish limpo. Licencas/notices .NET, NAudio e QRCoder acompanham
o pacote. A chave privada do APK nunca faz parte da distribuicao; o certificado
publico embutido na assinatura e normal e necessario para verificar o APK.

## Publicar

O GitHub CLI portatil fica no disco D, com configuracao em `.cache/github-cli`.
A autorizacao da conta ocorre pela pagina oficial de device login; nao cole senha
ou token no chat. A publicacao e uma acao externa e deve ser solicitada/aprovada.

Envie somente APK, ZIP, LEIA-ME e SHA256SUMS. Crie primeiro uma release em rascunho,
confira nomes, tamanhos e digests retornados pelo GitHub e so entao publique como
pre-release. Nao use `git add .` na pasta do projeto para distribuir binarios.

## Limites

Esta e uma distribuicao para testes, nao uma versao de loja: APK de desenvolvimento
e executavel Windows sem assinatura de editor. O amigo deve conferir a origem e
fazer seu proprio pareamento. O pacote e verificado no ambiente de desenvolvimento,
mas isso nao e um teste em uma instalacao Windows/Android limpa de outro fabricante.
