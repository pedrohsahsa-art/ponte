param([switch]$SkipAndroidBuild)
. "$PSScriptRoot\env.ps1"
if (!$SkipAndroidBuild) {
    & "$PSScriptRoot\build-android.ps1"
}
[xml]$project = Get-Content "$Root\windows\Ponte.Windows.csproj"
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versao invalida para empacotar.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$publish = Join-Path $Root ('.cache\share\publish-' + $stamp)
$stage = Join-Path $Root ('.cache\share\portable-' + $stamp)
$output = Join-Path $Root ('dist\share\ponte-' + $version + '-' + $stamp)
& dotnet publish "$Root\windows\Ponte.Windows.csproj" -c Release -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar o Windows limpo para distribuicao.' }
New-Item -ItemType Directory -Force "$stage\app", "$stage\licenses", $output | Out-Null

# Source is a NEW clean publish directory, never dist/windows or data. Only
# runtime binaries and its two known configuration files are permitted.
foreach ($file in Get-ChildItem $publish -File -Recurse) {
    if ($file.Extension -eq '.pdb') { continue }
    if ($file.Extension -notin @('.dll', '.exe') -and $file.Name -notin @('Ponte.Windows.deps.json', 'Ponte.Windows.runtimeconfig.json')) {
        throw "Arquivo inesperado no publish limpo: $($file.Name)"
    }
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Nao incluir links na distribuicao.' }
    $relative = $file.FullName.Substring($publish.Length).TrimStart('\')
    $destination = Join-Path "$stage\app" $relative
    New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
    Copy-Item $file.FullName $destination
}
Copy-Item "$Root\distribution\Iniciar Ponte.cmd" $stage
foreach ($name in @('LEIA-ME.txt', 'README.md', 'RELEASE-NOTES.md')) {
    $text = [IO.File]::ReadAllText("$Root\distribution\$name").Replace('{VERSION}', $version)
    [IO.File]::WriteAllText((Join-Path $output $name), $text, [Text.UTF8Encoding]::new($false))
}
Copy-Item "$output\LEIA-ME.txt" $stage
Copy-Item "$Root\tools\dotnet\LICENSE.txt" "$stage\licenses\dotnet-LICENSE.txt"
Copy-Item "$Root\tools\dotnet\ThirdPartyNotices.txt" "$stage\licenses\dotnet-ThirdPartyNotices.txt"
Copy-Item "$Root\.cache\nuget\naudio\2.2.1\license.txt" "$stage\licenses\NAudio-LICENSE.txt"
Copy-Item "$Root\distribution\licenses\QRCoder-LICENSE.txt" "$stage\licenses\QRCoder-LICENSE.txt"

$apk = Join-Path $output "ponte-$version-android.apk"
Copy-Item "$Root\dist\android\ponte.apk" $apk
& "$env:ANDROID_HOME\build-tools\36.0.0\apksigner.bat" verify $apk
if ($LASTEXITCODE -ne 0) { throw 'APK sem assinatura valida.' }
$metadata = & "$env:ANDROID_HOME\build-tools\36.0.0\aapt.exe" dump badging $apk
if ($LASTEXITCODE -ne 0 -or ($metadata -join "`n") -notmatch "versionName='$([regex]::Escape($version))'") { throw 'Versao do APK difere do Windows.' }

$zip = Join-Path $output "ponte-$version-windows-x64.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$created = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $stage -File -Recurse) {
        $entryName = $file.FullName.Substring($stage.Length).TrimStart('\').Replace('\', '/')
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($created, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $created.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    if ($archive.Entries.FullName -match '(^|[/\\])data[/\\]|\.dpapi$|\.pfx$|\.p12$|\.keystore$|\.jks$|\.pdb$') { throw 'Artefato privado nao permitido na distribuicao.' }
    if (!($archive.Entries.FullName -contains 'Iniciar Ponte.cmd') -or !($archive.Entries.FullName -contains 'app/Ponte.Windows.exe')) { throw 'Pacote Windows incompleto.' }
} finally { $archive.Dispose() }
$hashes = @($apk, $zip, "$output\LEIA-ME.txt") | ForEach-Object { $hash = Get-FileHash $_ -Algorithm SHA256; "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))" }
[IO.File]::WriteAllLines("$output\SHA256SUMS.txt", $hashes, [Text.UTF8Encoding]::new($false))
Write-Host 'PASS: Pacote limpo, sem dados/pareamentos/certificados/tokens/chaves privadas ou PDBs.'
Write-Host "Pacotes prontos para compartilhar: $output"
Get-ChildItem $output | Select-Object Name,Length | Format-Table -AutoSize
