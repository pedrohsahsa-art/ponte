# Ponte — downloads para testes

Aplicativo pessoal para conectar Android e Windows: arquivos e controle remoto
nos dois sentidos, com consentimento nos dispositivos.

## Baixar

Abra **Releases** e baixe:

- `ponte-{VERSION}-android.apk` para o telefone (Android 8+).
- `ponte-{VERSION}-windows-x64.zip` para Windows 10/11 x64.
- `SHA256SUMS.txt` e `LEIA-ME.txt` para verificacao e instrucoes.

Extraia o ZIP inteiro e execute **Iniciar Ponte.cmd**. O runtime .NET esta incluido.
Comece pareando pela rede local e aprove o telefone no seu proprio PC.
Cada computador gera uma identidade nova: os downloads nao incluem certificados,
pareamentos, arquivos pessoais ou tokens de quem desenvolveu o aplicativo.

**Build de testes:** o APK usa assinatura de desenvolvimento e o executavel Windows
nao tem assinatura de editor. Confira a origem e os hashes; nao desative protecoes
do sistema. PC → Android depende de MediaProjection e Acessibilidade, autorizadas
manualmente, e pode variar por fabricante.

**Biblioteca temporaria:** reabrir o programa Windows apaga apenas as copias internas
dos arquivos. Salve fora da biblioteca o que quiser manter.

Este repositorio distribui os pacotes de teste, nao e uma copia da pasta de trabalho
nem inclui dados privados ou chaves de assinatura.
