# Run as Administrator ONLY if Windows Firewall is blocking the connection.
. "$PSScriptRoot\env.ps1"
$exe = Join-Path $Root 'dist\windows\Ponte.Windows.exe'
if (!(Test-Path $exe)) { throw 'Compile o programa Windows primeiro.' }
$name = 'Ponte - HTTPS pessoal'
if (!(Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow -Protocol TCP -LocalPort 47831 -Program $exe -RemoteAddress LocalSubnet,'100.64.0.0/10' -Profile Any
}
$discovery = 'Ponte - descoberta LAN'
if (!(Get-NetFirewallRule -DisplayName $discovery -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $discovery -Direction Inbound -Action Allow -Protocol UDP -LocalPort 47832 -Program $exe -RemoteAddress LocalSubnet -Profile Private
}
Write-Host 'HTTPS limitado a rede local/Tailscale; descoberta UDP apenas na sub-rede privada. Nenhuma porta foi aberta no roteador.'
