. "$PSScriptRoot\env.ps1"
& dotnet run --project "$Root\tests\Ponte.Tests.csproj" -c Release -- --capture-benchmark
if ($LASTEXITCODE -ne 0) { throw 'Falha ao medir a captura nativa.' }
