. "$PSScriptRoot\env.ps1"
$apk = Join-Path $Root 'dist\android\ponte.apk'
$exe = Join-Path $Root 'dist\windows\Ponte.Windows.exe'
if (!(Test-Path $apk) -or !(Test-Path $exe)) { throw 'Compile os dois aplicativos primeiro.' }
$signer = Join-Path $env:ANDROID_HOME 'build-tools\36.0.0\apksigner.bat'
$signature = & $signer verify --print-certs $apk
if ($LASTEXITCODE -ne 0) { throw 'A assinatura do APK e invalida.' }
$digest = ($signature | Select-String '^Signer #1 certificate SHA-256 digest:').Line
if (!$digest) { throw 'Nao foi possivel ler a identidade de assinatura.' }
Write-Host 'PASS: Assinatura do APK verificada'
$backup = Get-ChildItem "$Root\dist\backups\ponte-*.apk" -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -First 1
if ($backup) {
    $previous = & $signer verify --print-certs $backup.FullName
    if ($LASTEXITCODE -ne 0) { throw 'O APK anterior nao passou na verificacao de assinatura.' }
    $previousDigest = ($previous | Select-String '^Signer #1 certificate SHA-256 digest:').Line
    if ($digest -ne $previousDigest) { throw 'A chave do APK mudou; nao instale como atualizacao sem investigar.' }
    Write-Host 'PASS: Chave de assinatura igual a da versao anterior; atualizacao preserva dados do app'
}
$badging = & "$env:ANDROID_HOME\build-tools\36.0.0\aapt.exe" dump badging $apk
if ($LASTEXITCODE -ne 0) { throw 'Falha ao ler os metadados do APK.' }
$package = ($badging | Select-String '^package:').Line
if ($package -notmatch "name='br.com.ponte'" -or $package -notmatch "versionCode='9'" -or $package -notmatch "versionName='0.7.1'") { throw "Metadados de versao inesperados: $package" }
Write-Host "PASS: $package"
Write-Host "Windows: $exe ($((Get-Item $exe).VersionInfo.FileVersion))"
Get-FileHash $apk -Algorithm SHA256 | Format-List Algorithm, Hash, Path
