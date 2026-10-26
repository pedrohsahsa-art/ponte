using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Ponte.Core;
using QRCoder;

namespace Ponte.Windows;

public partial class MainWindow : Window
{
    private void DragTitleBar(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        for (var source = e.OriginalSource as DependencyObject; source is not null;
             source = System.Windows.Media.VisualTreeHelper.GetParent(source))
            if (source is System.Windows.Controls.Button) return;
        if (e.ClickCount == 2) ToggleMaximize(sender, e);
        else if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void ToggleMaximize(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void NavigateSection(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedItem = ((sender as FrameworkElement)?.Tag as string) switch
        {
            "connect" => ConnectionTab,
            "files" => FilesTab,
            _ => DevicesTab
        };
    }
    private void ShowQuickHelp(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "1. Em Conectar, gere o QR Code e escaneie no Android. Aprove neste PC.\n\n2. Em Controlar, selecione o telefone. Autorize o PC ou abra a tela do Android.\n\n3. Em Arquivos, envie ou salve uma cópia. Salve fora da biblioteca antes de reabrir o Ponte.\n\nA autorização permanece até ser revogada. Para parar e revogar: Ctrl+Alt+F10.",
        "Ponte · Como usar", MessageBoxButton.OK, MessageBoxImage.Information);
    private void AudioPermissionChanged(object sender, RoutedEventArgs e) => remote?.SetAudioAllowed((sender as System.Windows.Controls.CheckBox)?.IsChecked == true);
    private DeviceRegistry devices = null!;
    private FileLibrary files = null!;
    private TransferServer? server;
    private X509Certificate2? certificate;
    private string? pairingLink;
    private bool stopping, stopped;
    private readonly string dataDirectory;
    private readonly Mutex instanceMutex = new(false, "Local\\Ponte.Personal.Windows");
    private bool ownsMutex;
    private RemoteAccess? remote;
    private string? remoteDeviceId;
    private readonly DispatcherTimer remoteTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private HwndSource? windowSource;
    private bool hotkeyRegistered;
    private volatile bool internetEnabled;
    private bool updatingInternet;
    private DuckDnsUpdater? ddns;
    private readonly AndroidScreenHub androidScreen = new();
    private void OpenAndroid(object sender, RoutedEventArgs e)
    {
        if (DevicesList.SelectedItem is not Device device) { MessageBox.Show(this, "Selecione um telefone pareado na lista."); return; }
        try { new AndroidViewer(androidScreen, device) { Owner = this }.Show(); }
        catch (InvalidOperationException ex) { MessageBox.Show(this, ex.Message); }
    }
    private async void SaveDdns(object sender, RoutedEventArgs e)
    {
        if (ddns is null) return;
        DdnsSave.IsEnabled = false;
        try { await ddns.ConfigureAsync(DdnsDomain.Text, DdnsToken.Password, DdnsEnabled.IsChecked == true); DdnsToken.Clear(); DdnsStatus.Text = "Configuração salva com proteção do Windows. Atualizando…"; }
        catch (ArgumentException ex) { DdnsStatus.Text = ex.Message; }
        catch (Exception) { DdnsStatus.Text = "Não foi possível salvar DDNS. Verifique o armazenamento; nenhum token foi exibido."; }
        finally { DdnsSave.IsEnabled = true; }
    }
    private void InternetPermissionChanged(object sender, RoutedEventArgs e)
    {
        if (server is null || updatingInternet) return;
        var enabled = InternetPermission.IsChecked == true;
        try
        {
            var path = Path.Combine(dataDirectory, "internet-enabled.json");
            File.WriteAllText(path + ".tmp", System.Text.Json.JsonSerializer.Serialize(enabled));
            File.Move(path + ".tmp", path, true);
            internetEnabled = enabled;
            if (!enabled) { remote?.Interrupt(); androidScreen.Interrupt(); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            internetEnabled = false; remote?.Interrupt();
            updatingInternet = true;
            try { InternetPermission.IsChecked = false; } finally { updatingInternet = false; }
            Status.Text = "Não foi possível salvar a opção de internet: " + ex.Message;
        }
    }
    private const int StopHotkeyId = 0x504F;

    public MainWindow()
    {
        InitializeComponent();
        // Published under dist/windows; development and publish both remain on D:.
        dataDirectory = Environment.GetEnvironmentVariable("PONTE_DATA_DIR") ?? @"D:\programations\ponte\data";
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        windowSource?.AddHook(WindowMessage);
        hotkeyRegistered = RegisterHotKey(windowSource!.Handle, StopHotkeyId, 0x4003, 0x79);
        if (!hotkeyRegistered) StopRemoteButton.Content = "Parar/revogar acesso";
    }
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == StopHotkeyId)
        { RevokeRemoteAccess(); handled = true; }
        return IntPtr.Zero;
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ownsMutex = instanceMutex.WaitOne(0);
            if (!ownsMutex) { MessageBox.Show("O Ponte já está aberto."); Close(); return; }
            Directory.CreateDirectory(dataDirectory);
            devices = new DeviceRegistry(dataDirectory);
            var libraryPath = Path.Combine(dataDirectory, "files");
            FileLibrary.BeginNewSession(libraryPath);
            files = new FileLibrary(libraryPath);
            certificate = CertificateIdentity.Load(dataDirectory);
            var addresses = NetworkAddressSelection.Order(NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses
                    .Select(a => new NetworkAddress(a.Address.ToString(), n.Name,
                        n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)),
                        NetworkAddressSelection.IsVirtual(n.Name, n.Description)))));
            HostAddress.ItemsSource = addresses.Select(a => a.Address).ToArray();
            HostAddress.Text = addresses.FirstOrDefault()?.Address ?? "127.0.0.1";
            NetworkHint.Text = addresses.Count > 0 ? $"Mesmo Wi-Fi/rede local: use {addresses[0].Address} ({addresses[0].InterfaceName}). Não use o IP do Radmin/VPN para conectar pelo Wi-Fi." : "Nenhum IPv4 de rede encontrado. Confira sua conexão.";
            remote = new RemoteAccess(NativeDesktop.Capture, NativeDesktop.Execute, NativeDesktop.IsAvailable,
                () => Dispatcher.InvokeAsync(RefreshRemoteStatus), Path.Combine(dataDirectory, "remote-permission.json"), DesktopKeyboardProbe.Read,
                NativeDesktop.Monitors, NativeDesktop.Capture, NativeDesktop.Execute, DesktopKeyboardProbe.ReadConfigured, NativeDesktop.ReleaseButtons);
            remoteDeviceId = remote.AllowedDeviceId;
            if (remoteDeviceId is not null && !devices.List().Any(d => d.Id == remoteDeviceId)) remote.Disable();
            var internetPath = Path.Combine(dataDirectory, "internet-enabled.json");
            internetEnabled = File.Exists(internetPath) && System.Text.Json.JsonSerializer.Deserialize<bool>(File.ReadAllText(internetPath));
            InternetPermission.IsChecked = internetEnabled;
            InternetAddress.Text = InternetConnectivity.StableAddresses().FirstOrDefault() ?? "Nenhum IPv6 público estável encontrado.";
            ddns = new DuckDnsUpdater(dataDirectory, () => InternetConnectivity.StableAddresses().FirstOrDefault(), text => Dispatcher.InvokeAsync(() => DdnsStatus.Text = text));
            DdnsDomain.Text = ddns.Domain; DdnsEnabled.IsChecked = ddns.Enabled;
            server = new TransferServer(devices, files, ApproveAsync, () => Dispatcher.InvokeAsync(RefreshLists), remote,
                text => Dispatcher.InvokeAsync(() => ConnectionStatus.Text = text), () => internetEnabled, () => ddns?.Hostname, androidScreen);
            await server.StartAsync(certificate);
            Status.Text = $"Conexão HTTPS ativa · {HostAddress.Text}:{TransferServer.Port} · mantenha esta janela aberta";
            PairButton.IsEnabled = true;
            EnableRemoteButton.IsEnabled = true;
            remoteTimer.Tick += (_, _) => RefreshRemoteStatus();
            remoteTimer.Start();
            SystemEvents.SessionSwitch += OnSessionSwitch;
            RefreshLists();
            RefreshRemoteStatus();
            if (devices.List().Count > 0) MainTabs.SelectedItem = DevicesTab;
            GeneratePairing(sender, e);
        }
        catch (Exception ex) { Status.Text = "Não foi possível iniciar: " + ex.Message; }
    }
    private void GeneratePairing(object sender, RoutedEventArgs e)
    {
        if (certificate is null) return;
        if (!IPAddress.TryParse(HostAddress.Text.Trim(), out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        { MessageBox.Show("Informe um endereço IPv4 válido deste computador."); return; }
        var ticket = devices.CreateTicket();
        pairingLink = $"ponte://pair?host={ip}&port={TransferServer.Port}&fingerprint={certificate.GetCertHashString(HashAlgorithmName.SHA256)}&code={ticket.Code}";
        using var qr = QRCodeGenerator.GenerateQrCode(pairingLink, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(qr);
        using var stream = new MemoryStream(png.GetGraphic(6));
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit();
        QrImage.Source = image;
        TicketStatus.Text = $"Válido até {ticket.ExpiresAt.LocalDateTime:HH:mm:ss}. Uso único.";
    }
    private Task<bool> ApproveAsync(string name, string address)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(60);
        Dispatcher.InvokeAsync(() =>
        {
            if (stopping || DateTimeOffset.UtcNow >= expiresAt) { result.TrySetResult(false); return; }
            // Remote input must not be able to approve another pairing dialog.
            using var pause = remote?.PauseForConsent();
            QrImage.Source = null;
            pairingLink = null;
            TicketStatus.Text = "Código utilizado. Gere outro para conectar mais um telefone.";
            var allowed = MessageBox.Show(this, $"Permitir que '{name}' ({address}) envie e baixe arquivos desta biblioteca?\n\nAprove somente se você iniciou este pedido no seu telefone.", "Autorizar telefone", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            result.TrySetResult(allowed && DateTimeOffset.UtcNow < expiresAt);
        });
        return WaitForApproval(result.Task);
    }
    private static async Task<bool> WaitForApproval(Task<bool> task)
    {
        try { return await task.WaitAsync(TimeSpan.FromSeconds(60)); }
        catch (TimeoutException) { return false; }
    }
    private void CopyPairing(object sender, RoutedEventArgs e)
    {
        if (pairingLink is not null) Clipboard.SetText(pairingLink);
    }
    private void RefreshLists()
    {
        if (files is null || devices is null) return;
        try
        {
            var selectedId = (DevicesList.SelectedItem as Device)?.Id;
            FilesList.ItemsSource = files.List();
            DevicesList.ItemsSource = devices.List();
            DevicesList.SelectedItem = devices.List().FirstOrDefault(d => d.Id == selectedId);
        }
        catch (Exception ex) { Status.Text = "Erro ao ler a biblioteca: " + ex.Message; }
    }
    private void RefreshClicked(object sender, RoutedEventArgs e) => RefreshLists();
    private async void AddFiles(object sender, RoutedEventArgs e)
    {
        if (files is null) return;
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Compartilhar com o telefone" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            foreach (var path in dialog.FileNames)
            {
                Status.Text = "Adicionando " + Path.GetFileName(path) + "…";
                await using var stream = File.OpenRead(path);
                await files.AddAsync(stream, Path.GetFileName(path), "Windows");
            }
            Status.Text = "Arquivos disponíveis para baixar no Android.";
            RefreshLists();
        }
        catch (Exception ex) { Status.Text = "Falha ao adicionar: " + ex.Message; }
    }
    private async void SaveFile(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is not SharedFile selected) return;
        var dialog = new SaveFileDialog { FileName = selected.Name, InitialDirectory = @"D:\", Title = "Salvar cópia do arquivo" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var entry = files.Find(selected.Id)!.Value;
            await using var input = File.OpenRead(entry.Path);
            await using var output = File.Create(dialog.FileName);
            await input.CopyToAsync(output);
            Status.Text = "Cópia salva em " + dialog.FileName;
        }
        catch (Exception ex) { Status.Text = "Falha ao salvar: " + ex.Message; }
    }
    private void RevokeDevice(object sender, RoutedEventArgs e)
    {
        if (DevicesList.SelectedItem is not Device selected) return;
        if (MessageBox.Show(this, $"Revogar o acesso de {selected.Name}?", "Revogar", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            devices.Revoke(selected.Id);
            androidScreen.Interrupt(selected.Id);
            if (selected.Id == remoteDeviceId) remote?.Disable();
            RefreshLists();
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void EnableRemote(object sender, RoutedEventArgs e)
    {
        if (remote is null || DevicesList.SelectedItem is not Device selected)
        { MessageBox.Show(this, "Selecione um telefone pareado na aba Dispositivos."); return; }
        // Stop BEFORE asking, otherwise a remote pointer could renew its own consent.
        using var pause = remote.PauseForConsent();
        if (MessageBox.Show(this, $"Permitir que '{selected.Name}' veja o monitor selecionado e controle mouse, arrastos, teclado e volume SEM PRAZO DE EXPIRAÇÃO?\n\nSomente este telefone será autorizado. A permissão será mantida ao reiniciar o Ponte, até você revogar manualmente. Áudio exige marcar a opção separada nesta janela.\n\nUse Parar/revogar" + (hotkeyRegistered ? " ou Ctrl+Alt+F10 para retirar o acesso." : " para retirar o acesso."),
            "Autorizar tela e controle", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { remoteDeviceId = selected.Id; remote.EnableFor(selected.Id); RefreshRemoteStatus(); }
        catch (RemoteAccessException) { Status.Text = "A área de trabalho está indisponível para controle. Desbloqueie o Windows e tente novamente."; }
    }
    private void StopRemote(object sender, RoutedEventArgs e) => RevokeRemoteAccess();
    private void RevokeRemoteAccess()
    {
        androidScreen.Interrupt();
        try { remote?.Disable(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status.Text = "Acesso interrompido nesta execução, mas não foi possível salvar a revogação. Verifique o disco antes de reabrir: " + ex.Message; }
    }
    private void RefreshRemoteStatus()
    {
        if (remote is null || remoteDeviceId is null) return;
        var state = remote.StateFor(remoteDeviceId);
        var name = devices.List().FirstOrDefault(d => d.Id == remoteDeviceId)?.Name ?? "telefone";
        RemoteStatus.Text = !state.Enabled ? "Desativado. O telefone não pode ver nem controlar a tela." :
            $"{(state.Connected ? "COMPARTILHANDO" : "Autorizado")}: {name} · acesso permanente até você revogar";
        RemotePanel.Background = state.Connected ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 246, 232)) : System.Windows.Media.Brushes.White;
        StopRemoteButton.IsEnabled = state.Enabled;
        EnableRemoteButton.IsEnabled = !state.Enabled && !stopping;
        Title = state.Enabled ? "Ponte · TELA E CONTROLE AUTORIZADOS" : "Ponte · Seu Android e seu PC";
    }
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        { remote?.Interrupt(); androidScreen.Interrupt(); }
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (stopped) return;
        e.Cancel = true;
        if (stopping) return;
        stopping = true;
        remoteTimer.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        remote?.Interrupt();
        androidScreen.Interrupt();
        if (hotkeyRegistered && windowSource is not null) UnregisterHotKey(windowSource.Handle, StopHotkeyId);
        windowSource?.RemoveHook(WindowMessage);
        IsEnabled = false;
        try { if (ddns is not null) await ddns.DisposeAsync(); if (server is not null) await server.StopAsync(); }
        finally
        {
            certificate?.Dispose();
            if (ownsMutex) instanceMutex.ReleaseMutex();
            instanceMutex.Dispose();
            stopped = true;
            Close();
        }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
}
