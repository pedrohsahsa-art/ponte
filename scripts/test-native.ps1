. "$PSScriptRoot\env.ps1"
Write-Host 'Uma janela de teste isolada vai abrir por alguns segundos. Nao usa seus documentos e nao altera o volume.'
& dotnet run --project "$Root\tests\Ponte.Tests.csproj" -c Release -- --native-smoke
if ($LASTEXITCODE -ne 0) { throw 'Falha no teste nativo de tela/mouse/teclado.' }
