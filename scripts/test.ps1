. "$PSScriptRoot\env.ps1"
& dotnet run --project "$Root\tests\Ponte.Tests.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Os testes falharam.' }
