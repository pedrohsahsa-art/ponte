. "$PSScriptRoot\env.ps1"
$staging = Join-Path $Root ('.cache\publish\windows-' + [Guid]::NewGuid().ToString('N'))
& dotnet publish "$Root\windows\Ponte.Windows.csproj" -c Release -r win-x64 --self-contained true -o $staging
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao do Windows.' }
$destination = Join-Path $Root 'dist\windows'
Get-Process 'Ponte.Windows' -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.Path -eq (Join-Path $destination 'Ponte.Windows.exe')) {
        $null = $_.CloseMainWindow()
        if (!$_.WaitForExit(10000)) { throw 'Feche o Ponte antes de atualizar; nenhum processo sera forcado a encerrar.' }
    }
}
New-Item -ItemType Directory -Force "$Root\dist\backups" | Out-Null
if (Test-Path $destination) {
    Move-Item $destination (Join-Path $Root ('dist\backups\windows-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')))
}
Move-Item $staging $destination
Write-Host 'Windows atualizado. Versao anterior preservada em dist\backups; dados e pareamentos mantidos.'
