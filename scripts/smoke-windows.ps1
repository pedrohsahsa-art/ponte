param([switch]$WithGrant, [switch]$Screenshot, [switch]$WithInternet)
. "$PSScriptRoot\env.ps1"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$folder = Join-Path $Root ('.cache\smoke\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $folder | Out-Null
$library = Join-Path $folder 'files'
New-Item -ItemType Directory -Force $library | Out-Null
$fileId = [Guid]::NewGuid().ToString('N')
[IO.File]::WriteAllBytes((Join-Path $library ($fileId + '.bin')), [byte[]]@(1,2,3))
[IO.File]::WriteAllText((Join-Path $library ($fileId + '.json')), (ConvertTo-Json -InputObject @{ Id = $fileId; Name = 'session-test.txt'; Size = 3; CreatedAt = [DateTimeOffset]::UtcNow.ToString('O'); Source = 'test' }))
[IO.File]::WriteAllText((Join-Path $folder 'saved-original.txt'), 'keep outside the library')
if ($WithInternet) { [IO.File]::WriteAllText((Join-Path $folder 'internet-enabled.json'), 'true') }
if ($WithGrant) {
    $id = [Guid]::NewGuid().ToString('N')
    $device = @{ Id = $id; Name = 'Telefone de teste - permanente'; TokenHash = ('0' * 64); PairedAt = [DateTimeOffset]::UtcNow.ToString('O') }
    [IO.File]::WriteAllText((Join-Path $folder 'devices.json'), (ConvertTo-Json -InputObject @($device)))
    [IO.File]::WriteAllText((Join-Path $folder 'remote-permission.json'), (ConvertTo-Json -InputObject @{ DeviceId = $id }))
}
function Test-Launch {
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = Join-Path $Root 'dist\windows\Ponte.Windows.exe'
$start.WorkingDirectory = $Root
$start.UseShellExecute = $false
$start.EnvironmentVariables['PONTE_DATA_DIR'] = $folder
$process = [Diagnostics.Process]::Start($start)
try {
    $null = $process.WaitForInputIdle(15000)
    $statusText = ''
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) { throw "O programa encerrou com codigo $($process.ExitCode)." }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, 'Status')
            $element = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
            if ($element) {
                $statusText = $element.Current.Name
                if ($statusText -like '*HTTPS ativa*') { break }
                if ($statusText -like '*poss*vel iniciar*') { throw $statusText }
            }
        }
        Start-Sleep -Milliseconds 200
    }
    if ($statusText -notlike '*HTTPS ativa*') { throw "A janela nao ficou pronta: $statusText" }
    Write-Host "PASS: WPF startup - $statusText"
    $connectionTabCondition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, 'ConnectionTab')
    $connectionTab = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $connectionTabCondition)
    if (!$connectionTab) { throw 'A aba Conexao nao foi encontrada.' }
    $connectionTab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $qrCondition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, 'TicketStatus')
    $ticket = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $qrCondition)
    if (!$ticket -or $ticket.Current.Name -notlike '*Uso *nico*') { throw 'O QR Code nao foi gerado.' }
    Write-Host 'PASS: QR Code generated at startup'
    $remoteCondition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, 'RemoteStatus')
    $remote = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $remoteCondition)
    if (!$remote) { throw 'O painel de controle remoto nao foi encontrado.' }
    if ($WithGrant -and $remote.Current.Name -notlike '*acesso permanente*') { throw 'A autorizacao permanente nao foi restaurada.' }
    if (!$WithGrant -and $remote.Current.Name -notlike 'Desativado*') { throw 'Uma instalacao nova precisa iniciar sem autorizacao.' }
    $stopCondition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, 'StopRemoteButton')
    $stop = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $stopCondition)
    if (!$stop -or $stop.Current.IsEnabled -ne [bool]$WithGrant) { throw 'O estado do botao de revogacao esta incorreto.' }
    Write-Host "PASS: Remote-sharing panel correctly reflects persisted grant: $([bool]$WithGrant)"
    $optionsCondition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, 'InternetOptions')
    $options = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $optionsCondition)
    if (!$options) { throw 'As opcoes avancadas nao foram encontradas.' }
    $options.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $internetCondition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, 'InternetPermission')
    $internetControl = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $internetCondition)
    if (!$internetControl) { throw 'A opcao de internet nao foi encontrada.' }
    $internetToggle = $internetControl.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    $isOn = $internetToggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::On
    if ($isOn -ne [bool]$WithInternet) { throw 'A permissao de internet nao refletiu a preferencia salva.' }
    Write-Host "PASS: Separate Internet opt-in restored correctly: $isOn"
    foreach ($tabId in @('FilesTab', 'DevicesTab')) {
        $tabCondition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $tabId)
        $tab = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $tabCondition)
        if (!$tab) { throw "A secao $tabId nao foi encontrada." }
        $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        if (!$tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw "A navegacao para $tabId falhou." }
    }
    Write-Host 'PASS: Files and Control navigation is available'
    if ($Screenshot) {
        Add-Type -AssemblyName System.Drawing
        if (!('PonteUiCapture' -as [type])) {
            Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PonteUiCapture {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
}
'@
        }
        $rect = New-Object PonteUiCapture+Rect
        $null = [PonteUiCapture]::GetWindowRect($process.MainWindowHandle, [ref]$rect)
        $image = New-Object Drawing.Bitmap(($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
        $graphics = [Drawing.Graphics]::FromImage($image)
        $dc = $graphics.GetHdc()
        try { $null = [PonteUiCapture]::PrintWindow($process.MainWindowHandle, $dc, 2) }
        finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
        $uiFolder = Join-Path $Root '.cache\ui'
        New-Item -ItemType Directory -Force $uiFolder | Out-Null
        $image.Save((Join-Path $uiFolder 'windows.png'), [Drawing.Imaging.ImageFormat]::Png)
        $image.Dispose()
        Write-Host "UI screenshot (isolated test data): $uiFolder\windows.png"
    }
    Write-Host "Smoke-test data: $folder"
} finally {
    if (!$process.HasExited) {
        $null = $process.CloseMainWindow()
        if (!$process.WaitForExit(10000)) { throw 'A janela de teste nao encerrou no prazo esperado.' }
    }
}
}
Test-Launch
if (Test-Path (Join-Path $library ($fileId + '.bin'))) { throw 'A copia interna da sessao anterior nao foi removida.' }
if (!(Test-Path (Join-Path $folder 'saved-original.txt'))) { throw 'O teste detectou remocao fora da biblioteca.' }
Write-Host 'PASS: New PC session removes previous internal copies and preserves files outside the library'
if ($WithGrant) {
    Test-Launch
    Write-Host 'PASS: Explicit grant survives actual application close and restart'
}
