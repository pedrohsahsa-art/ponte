. "$PSScriptRoot\env.ps1"
Write-Host 'Testa somente a saida WASAPI por HTTPS, com reproducao de silencio. Sem microfone e sem gravar audio em disco.'
dotnet run --project "$Root\tests\Ponte.Tests.csproj" -c Release -- --audio-smoke
if ($LASTEXITCODE -ne 0) { throw 'Falha no teste de audio.' }
