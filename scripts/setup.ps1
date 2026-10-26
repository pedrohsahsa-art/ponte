param([switch]$Android, [switch]$AcceptAndroidLicenses)
. "$PSScriptRoot\env.ps1"
function Download($Url, $Target, $Hash = '', $Algorithm = 'SHA256') {
    if (!(Test-Path $Target)) {
        & curl.exe --fail --location --retry 3 --output $Target $Url
        if ($LASTEXITCODE -ne 0) { throw "Falha no download: $Url" }
    }
    if ($Hash -and (Get-FileHash $Target -Algorithm $Algorithm).Hash -ne $Hash) {
        throw "Checksum incorreto: $Target. Remova apenas esse arquivo e tente novamente."
    }
}
if (!(Test-Path "$env:DOTNET_ROOT\dotnet.exe")) {
    $meta = Invoke-RestMethod 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json'
    $sdk = $meta.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $meta.'latest-sdk' } | Select-Object -First 1
    $file = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
    Download $file.url "$Root\.downloads\dotnet.zip" $file.hash 'SHA512'
    Expand-Archive "$Root\.downloads\dotnet.zip" $env:DOTNET_ROOT -Force
}
if ($Android) {
    if (!(Test-Path "$env:JAVA_HOME\bin\java.exe")) {
        $assets = Invoke-RestMethod 'https://api.adoptium.net/v3/assets/latest/17/hotspot?architecture=x64&image_type=jdk&os=windows'
        $package = $assets[0].binary.package
        Download $package.link "$Root\.downloads\jdk.zip" $package.checksum
        Expand-Archive "$Root\.downloads\jdk.zip" "$Root\tools\jdk-extract" -Force
        $folder = Get-ChildItem "$Root\tools\jdk-extract" -Directory | Select-Object -First 1
        Move-Item $folder.FullName $env:JAVA_HOME
    }
    if (!(Test-Path "$Root\tools\gradle\bin\gradle.bat")) {
        $rawHash = (Invoke-WebRequest 'https://services.gradle.org/distributions/gradle-8.13-bin.zip.sha256' -UseBasicParsing).Content
        $hash = if ($rawHash -is [byte[]]) { [Text.Encoding]::UTF8.GetString($rawHash).Trim() } else { $rawHash.Trim() }
        Download 'https://services.gradle.org/distributions/gradle-8.13-bin.zip' "$Root\.downloads\gradle.zip" $hash
        Expand-Archive "$Root\.downloads\gradle.zip" "$Root\tools" -Force
        Move-Item "$Root\tools\gradle-8.13" "$Root\tools\gradle"
    }
    if (!(Test-Path "$env:ANDROID_HOME\cmdline-tools\latest\bin\sdkmanager.bat")) {
        Download 'https://dl.google.com/android/repository/commandlinetools-win-13114758_latest.zip' "$Root\.downloads\android-tools.zip"
        New-Item -ItemType Directory -Force "$env:ANDROID_HOME\cmdline-tools" | Out-Null
        Expand-Archive "$Root\.downloads\android-tools.zip" "$Root\tools\android-extract" -Force
        Move-Item "$Root\tools\android-extract\cmdline-tools" "$env:ANDROID_HOME\cmdline-tools\latest"
    }
    Write-Host 'O Android SDK pede aceite das licencas abaixo. Leia e responda no terminal.'
    if ($AcceptAndroidLicenses) {
        (1..20 | ForEach-Object { 'y' }) | & "$env:ANDROID_HOME\cmdline-tools\latest\bin\sdkmanager.bat" --licenses
    } else {
        & "$env:ANDROID_HOME\cmdline-tools\latest\bin\sdkmanager.bat" --licenses
    }
    if ($LASTEXITCODE -ne 0) { throw 'As licencas do Android SDK precisam ser aceitas.' }
    & "$env:ANDROID_HOME\cmdline-tools\latest\bin\sdkmanager.bat" 'platform-tools' 'platforms;android-36' 'build-tools;36.0.0'
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao preparar o Android SDK.' }
}
Write-Host 'Ferramentas preparadas no disco D.'
