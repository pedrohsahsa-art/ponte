using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Ponte.Core;
using Ponte.Windows;

static class NativeDesktopSmoke
{
    public static Task RunAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                if (!NativeDesktop.IsAvailable()) throw new Exception("Desbloqueie o Windows para executar o teste nativo.");
                var input = new TextBox { Margin = new Thickness(15), Height = 40, FontSize = 18 };
                var password = new PasswordBox { Margin = new Thickness(15, 0, 15, 0), Height = 30 };
                var button = new Button { Margin = new Thickness(15), Padding = new Thickness(12), Content = "Alvo de clique · somente teste" };
                var clicks = 0;
                button.Click += (_, _) => clicks++;
                var panel = new StackPanel();
                panel.Children.Add(new TextBlock { Text = "Teste do Ponte em janela isolada. Fecha sozinho.", Margin = new Thickness(15) });
                panel.Children.Add(input);
                panel.Children.Add(password);
                panel.Children.Add(button);
                window = new Window { Title = "Ponte · TESTE LOCAL", Content = panel, Width = 520, Height = 310, Left = 80, Top = 80, WindowStartupLocation = WindowStartupLocation.Manual };
                var previousWindow = GetForegroundWindow();
                GetCursorPos(out var previousCursor);
                var dispatcher = Dispatcher.CurrentDispatcher;
                window.Closed += (_, _) => dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                window.Loaded += (_, _) => dispatcher.BeginInvoke(new Action(async () =>
                {
                    var handle = new WindowInteropHelper(window).Handle;
                    void RequireOwnForeground()
                    {
                        if (GetForegroundWindow() != handle) throw new Exception("Teste interrompido: a janela de teste perdeu o foco. Nenhum comando será enviado a outra janela.");
                        if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0))
                            throw new Exception("Solte Shift/Ctrl/Alt/Windows e execute o teste novamente.");
                    }
                    try
                    {
                        // The coding harness launches in the background. Temporarily
                        // attach input queues to activate ONLY this visible test window.
                        // No input is sent unless the foreground check below succeeds.
                        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                        var ownThread = GetCurrentThreadId();
                        var attached = foregroundThread != 0 && foregroundThread != ownThread && AttachThreadInput(ownThread, foregroundThread, true);
                        try
                        {
                            window.Topmost = true;
                            window.Activate();
                            SetForegroundWindow(handle);
                            input.Focus();
                            Keyboard.Focus(input);
                            window.Topmost = false;
                        }
                        finally { if (attached) AttachThreadInput(ownThread, foregroundThread, false); }
                        await Task.Delay(350);
                        RequireOwnForeground();
                        var editTarget = await Task.Run(() => DesktopKeyboardProbe.Read());
                        if (!editTarget.Available || !editTarget.Editable || editTarget.Password || string.IsNullOrEmpty(editTarget.FocusId)) throw new Exception("O campo editável de teste não foi detectado por acessibilidade.");
                        Console.WriteLine("PASS: Editable focus detected without reading its contents");
                        var frame = NativeDesktop.Capture();
                        if (frame.Width is < 1 or > 1920 || frame.Height is < 1 or > 1080 || frame.Jpeg.Length < 4 || frame.Jpeg[0] != 0xFF || frame.Jpeg[1] != 0xD8)
                            throw new Exception("Captura JPEG inválida.");
                        using (var stream = new MemoryStream(frame.Jpeg))
                        using (var image = System.Drawing.Image.FromStream(stream))
                            if (image.Width != frame.Width || image.Height != frame.Height) throw new Exception("Dimensões JPEG inconsistentes.");
                        Console.WriteLine($"PASS: Native primary-screen JPEG {frame.Width}x{frame.Height}; image kept in memory only");
                        var monitors = NativeDesktop.Monitors();
                        if (monitors.Count == 0 || !monitors.Any(m => m.Primary) || monitors.Any(m => m.Width < 1 || m.Height < 1)) throw new Exception("Invalid monitor inventory");
                        foreach (var monitor in monitors)
                        foreach (var profile in new[] { "fluid", "balanced", "sharp" })
                        {
                            var settings = new StreamSettings(monitor.Id, profile);
                            var configured = NativeDesktop.Capture(settings);
                            if (configured.MonitorId != settings.MonitorId || configured.Width > settings.Limits.Width || configured.Height > settings.Limits.Height) throw new Exception("Capture did not respect selected monitor/profile");
                        }
                        Console.WriteLine($"PASS: Native monitor enumeration ({monitors.Count}) and all three capture quality profiles");
                        RequireOwnForeground();
                        NativeDesktop.Execute(new RemoteCommand("text", Text: "Ponte ação 123"));
                        await Task.Delay(180);
                        if (input.Text != "Ponte ação 123") throw new Exception("A digitação Unicode não chegou à caixa de teste.");
                        Console.WriteLine("PASS: Native Unicode keyboard input into isolated test window");
                        RequireOwnForeground();
                        NativeDesktop.Execute(new RemoteCommand("key", Key: "backspace"));
                        await Task.Delay(120);
                        if (input.Text != "Ponte ação 12") throw new Exception("Backspace não foi aplicado à caixa de teste.");
                        Console.WriteLine("PASS: Native special key input");
                        RequireOwnForeground();
                        NativeDesktop.Execute(new RemoteCommand("edit", Delta: 2, Text: "ao vivo"));
                        await Task.Delay(130);
                        if (input.Text != "Ponte ação ao vivo") throw new Exception("A edição ao vivo não removeu/substituiu o sufixo do campo.");
                        Console.WriteLine("PASS: Native live edit applies backspaces and Unicode replacement in order");
                        RequireOwnForeground();
                        NativeDesktop.Execute(new RemoteCommand("edit", Text: "😀"));
                        await Task.Delay(90);
                        NativeDesktop.Execute(new RemoteCommand("edit", Delta: 1));
                        await Task.Delay(100);
                        if (input.Text != "Ponte ação ao vivo") throw new Exception("A exclusão de emoji deixou um par Unicode incompleto.");
                        Console.WriteLine("PASS: Native emoji insertion/backspace preserves complete Unicode");
                        password.Focus();
                        Keyboard.Focus(password);
                        await Task.Delay(100);
                        var passwordTarget = await Task.Run(() => DesktopKeyboardProbe.Read());
                        if (!passwordTarget.Editable || !passwordTarget.Password) throw new Exception("O campo de senha não foi identificado somente por metadados.");
                        Console.WriteLine("PASS: Password field identified by metadata without reading a password");
                        RequireOwnForeground();
                        var point = button.PointToScreen(new System.Windows.Point(button.ActualWidth / 2, button.ActualHeight / 2));
                        var size = NativeDesktop.GetPrimarySize();
                        if (point.X < 0 || point.X >= size.Width || point.Y < 0 || point.Y >= size.Height) throw new Exception("A janela de teste não está no monitor principal.");
                        NativeDesktop.Execute(new RemoteCommand("move", point.X / (size.Width - 1), point.Y / (size.Height - 1)));
                        await Task.Delay(80);
                        GetCursorPos(out var mapped);
                        Console.WriteLine($"Mouse mapping: target {point.X:F0},{point.Y:F0}; actual {mapped.X},{mapped.Y}; primary {size.Width}x{size.Height}; monitors {string.Join("; ", monitors.Select(m => $"{m.Id}:{m.Left},{m.Top},{m.Width}x{m.Height}"))}");
                        NativeDesktop.Execute(new RemoteCommand("click", point.X / (size.Width - 1), point.Y / (size.Height - 1)));
                        await Task.Delay(150);
                        if (clicks != 1) throw new Exception("O clique nativo não atingiu o botão de teste.");
                        Console.WriteLine("PASS: Native absolute mouse click into isolated test window");
                        var buttonTarget = await Task.Run(() => DesktopKeyboardProbe.Read());
                        if (buttonTarget.Editable) throw new Exception("Um botão foi classificado como campo de texto.");
                        Console.WriteLine("PASS: Non-editable controls do not request the phone keyboard");
                        RequireOwnForeground();
                        var x = point.X / (size.Width - 1); var y = point.Y / (size.Height - 1);
                        try
                        {
                            NativeDesktop.Execute(new RemoteCommand("mousedown", x, y));
                            await Task.Delay(70);
                            if ((GetAsyncKeyState(1) & 0x8000) == 0) throw new Exception("Mouse down was not held");
                            NativeDesktop.Execute(new RemoteCommand("move", x, y));
                            NativeDesktop.Execute(new RemoteCommand("mouseup", x, y));
                            await Task.Delay(70);
                            if ((GetAsyncKeyState(1) & 0x8000) != 0 || clicks != 2) throw new Exception("Held button was not released over isolated test control");
                        }
                        finally { NativeDesktop.ReleaseButtons(); }
                        Console.WriteLine("PASS: Actual mouse down/move/up state is scoped to isolated test window");
                        var secondary = monitors.FirstOrDefault(m => !m.Primary);
                        if (secondary is not null)
                        {
                            window.Left = secondary.Left + 80; window.Top = secondary.Top + 80;
                            await Task.Delay(180);
                            RequireOwnForeground();
                            var secondaryPoint = button.PointToScreen(new System.Windows.Point(button.ActualWidth / 2, button.ActualHeight / 2));
                            var settings = new StreamSettings(secondary.Id);
                            var sx = (secondaryPoint.X - secondary.Left) / (secondary.Width - 1);
                            var sy = (secondaryPoint.Y - secondary.Top) / (secondary.Height - 1);
                            NativeDesktop.Execute(new RemoteCommand("move", sx, sy), settings);
                            await Task.Delay(80);
                            NativeDesktop.Execute(new RemoteCommand("click", sx, sy), settings);
                            await Task.Delay(130);
                            if (clicks != 3) throw new Exception("Selected-secondary-monitor input mapping failed");
                            input.Focus(); Keyboard.Focus(input);
                            await Task.Delay(100);
                            var field = input.PointToScreen(new System.Windows.Point(input.ActualWidth / 2, input.ActualHeight / 2));
                            var target = await Task.Run(() => DesktopKeyboardProbe.ReadConfigured(settings, (field.X - secondary.Left) / (secondary.Width - 1), (field.Y - secondary.Top) / (secondary.Height - 1)));
                            if (!target.Editable) throw new Exception("Selected-secondary-monitor editable field was not detected");
                            Console.WriteLine("PASS: Actual secondary monitor (negative desktop coordinates) capture, click and keyboard metadata");
                        }
                        completion.TrySetResult();
                    }
                    catch (Exception ex) { completion.TrySetException(ex); }
                    finally
                    {
                        var ownForeground = GetForegroundWindow() == handle;
                        if (ownForeground) SetCursorPos(previousCursor.X, previousCursor.Y);
                        window.Close();
                        if (ownForeground && previousWindow != IntPtr.Zero) SetForegroundWindow(previousWindow);
                    }
                }));
                window.Show();
                Dispatcher.Run();
            }
            catch (Exception ex) { window?.Close(); completion.TrySetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint thread, uint otherThread, bool attach);
}
