$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$env:DOTNET_ROOT = Join-Path $Root 'tools\dotnet'
$env:DOTNET_CLI_HOME = Join-Path $Root '.cache\dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $Root '.cache\nuget'
$env:JAVA_HOME = Join-Path $Root 'tools\jdk'
$env:ANDROID_HOME = Join-Path $Root 'tools\android-sdk'
$env:ANDROID_SDK_ROOT = $env:ANDROID_HOME
$env:ANDROID_USER_HOME = Join-Path $Root '.cache\android'
$env:GRADLE_USER_HOME = Join-Path $Root '.cache\gradle'
$env:TEMP = Join-Path $Root '.cache\temp'
$env:TMP = $env:TEMP
$env:PATH = "$env:DOTNET_ROOT;$env:JAVA_HOME\bin;$Root\tools\gradle\bin;$env:ANDROID_HOME\platform-tools;$env:PATH"
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
