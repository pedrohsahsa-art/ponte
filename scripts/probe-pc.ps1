param([Parameter(Mandatory = $true)][string]$Address)
. "$PSScriptRoot\env.ps1"
& dotnet run --project "$Root\tests\Ponte.Tests.csproj" -c Release -- --probe-pc $Address "$Root\data"
if ($LASTEXITCODE -ne 0) { throw 'O servidor local nao respondeu corretamente. Confira se o Ponte esta aberto.' }
