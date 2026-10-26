. "$PSScriptRoot\env.ps1"
& dotnet build "$Root\tests\Ponte.Tests.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o servidor de teste.' }
$folder = Join-Path $Root ('.cache\android-protocol\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $folder | Out-Null
$config = Join-Path $folder 'config.json'
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$start.Arguments = '"' + "$Root\tests\bin\Release\net10.0-windows\Ponte.Tests.dll" + '" --android-host "' + $config + '"'
$start.UseShellExecute = $false
$start.RedirectStandardInput = $true
$process = [Diagnostics.Process]::Start($start)
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (!(Test-Path $config)) {
        if ($process.HasExited) { throw 'O servidor de teste encerrou antes de ficar pronto.' }
        if ([DateTime]::UtcNow -ge $deadline) { throw 'O servidor de teste nao iniciou no prazo esperado.' }
        Start-Sleep -Milliseconds 100
    }
    $env:PONTE_TEST_CONFIG = $config
    & "$Root\tools\gradle\bin\gradle.bat" -p "$Root\android" --no-daemon --rerun-tasks testDebugUnitTest
    if ($LASTEXITCODE -ne 0) { throw 'Falha no teste real do cliente Kotlin com o servidor Windows.' }
    Copy-Item "$Root\android\app\build\test-results\testDebugUnitTest" (Join-Path $folder 'results') -Recurse
    Write-Host 'PASS: Kotlin/OkHttp -> Windows HTTPS, pareamento, pin TLS, arquivos, sessao de tela e comandos remotos. Tela sintetica e adaptador de entrada simulado neste teste.'
} finally {
    Remove-Item Env:\PONTE_TEST_CONFIG -ErrorAction SilentlyContinue
    if (!$process.HasExited) {
        $process.StandardInput.WriteLine('stop')
        $process.StandardInput.Flush()
        if (!$process.WaitForExit(10000)) { throw 'O servidor de teste nao encerrou no prazo esperado.' }
    }
}
