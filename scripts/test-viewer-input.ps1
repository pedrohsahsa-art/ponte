. "$PSScriptRoot\env.ps1"
Write-Host 'Abre um visualizador isolado com imagem sintetica e testa somente cliques/arrasto nessa janela. Nenhum comando vai ao telefone real.'
dotnet run --project "$Root\tests\Ponte.Tests.csproj" -c Release -- --viewer-input-smoke
if ($LASTEXITCODE -ne 0) { throw 'Falha no teste nativo do visualizador Android.' }
