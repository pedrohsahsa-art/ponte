. "$PSScriptRoot\env.ps1"
& "$Root\tools\gradle\bin\gradle.bat" -p "$Root\android" --no-daemon lintDebug testDebugUnitTest assembleDebug
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao do Android.' }
New-Item -ItemType Directory -Force "$Root\dist\android" | Out-Null
if (Test-Path "$Root\dist\android\ponte.apk") {
    New-Item -ItemType Directory -Force "$Root\dist\backups" | Out-Null
    Copy-Item "$Root\dist\android\ponte.apk" (Join-Path $Root ('dist\backups\ponte-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.apk'))
}
Copy-Item "$Root\android\app\build\outputs\apk\debug\app-debug.apk" "$Root\dist\android\ponte.apk" -Force
