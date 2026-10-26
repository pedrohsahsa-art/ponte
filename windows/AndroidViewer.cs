using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ponte.Core;

namespace Ponte.Windows;

public sealed class AndroidViewer : Window
{
    private readonly AndroidScreenHub hub;
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock status = new() { Text = "No telefone, toque em Compartilhar Android e autorize a captura.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), Height = 76 };
    private Point? down;
    public AndroidViewer(AndroidScreenHub hub, Device device)
    {
        this.hub = hub;
        Title = "Ponte · " + device.Name; Width = 500; Height = 850; MinWidth = 360; MinHeight = 500;
        Background = new SolidColorBrush(Color.FromRgb(236, 233, 216));
        var grid = new DockPanel { Margin = new Thickness(10) };
        var controls = new WrapPanel(); DockPanel.SetDock(controls, Dock.Top);
        foreach (var (label, kind) in new[] { ("Voltar", "back"), ("Início", "home"), ("Recentes", "recents") })
        { var button = new Button { Content = label }; button.Click += (_, _) => Send(new(kind)); controls.Children.Add(button); }
        var stop = new Button { Content = "Parar", Background = Brushes.IndianRed }; stop.Click += (_, _) => Close(); controls.Children.Add(stop);
        grid.Children.Add(controls);
        DockPanel.SetDock(status, Dock.Bottom); grid.Children.Add(status);
        var editor = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(editor, Dock.Bottom);
        var send = new Button { Content = "Aplicar texto" }; DockPanel.SetDock(send, Dock.Right); editor.Children.Add(send);
        var text = new TextBox { ToolTip = "Substitui o texto do campo focado. Não atua em senhas nem permissões.", MaxLength = 1000 }; editor.Children.Add(text);
        send.Click += (_, _) => { if (Send(new("text", Text: text.Text))) text.Clear(); };
        grid.Children.Add(editor);
        var viewport = new Border { Background = Brushes.Black, BorderBrush = new SolidColorBrush(Color.FromRgb(127, 157, 185)), BorderThickness = new Thickness(1), Child = image, ClipToBounds = true };
        grid.Children.Add(viewport); Content = grid;
        var lease = hub.OpenViewer(device.Id, Frame, value => Dispatcher.InvokeAsync(() => { status.Text = value; if (value.StartsWith("Compartilhamento encerrado")) image.Source = null; }));
        Closed += (_, _) => lease.Dispose();
        image.MouseLeftButtonDown += (_, e) => { down = Map(e.GetPosition(image)); if (down is not null) image.CaptureMouse(); };
        image.MouseLeftButtonUp += (_, e) =>
        {
            var startPoint = down; var end = Map(e.GetPosition(image)); image.ReleaseMouseCapture();
            if (startPoint is Point start && end is Point finish)
                Send((start - finish).Length < .015 ? new("tap", start.X, start.Y) : new("swipe", start.X, start.Y, finish.X, finish.Y));
            down = null;
        };
        image.MouseRightButtonUp += (_, _) => Send(new("back"));
        image.LostMouseCapture += (_, _) => down = null;
    }
    private bool Send(AndroidCommand command)
    {
        var accepted = hub.Send(command);
        status.Text = accepted ? "Comando enviado. Aguardando resposta do Android…" : "Comando não enviado. Confira se o telefone está compartilhando e a conexão está ativa.";
        return accepted;
    }
    private Point? Map(Point point)
    {
        if (image.Source is not BitmapSource bitmap || image.ActualWidth <= 0 || image.ActualHeight <= 0) return null;
        var scale = Math.Min(image.ActualWidth / bitmap.PixelWidth, image.ActualHeight / bitmap.PixelHeight);
        var width = bitmap.PixelWidth * scale; var height = bitmap.PixelHeight * scale;
        var x = (point.X - (image.ActualWidth - width) / 2) / width;
        var y = (point.Y - (image.ActualHeight - height) / 2) / height;
        return x is >= 0 and <= 1 && y is >= 0 and <= 1 ? new Point(x, y) : null;
    }
    private void Frame(byte[] jpeg)
    {
        using var stream = new MemoryStream(jpeg);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        // At most one UI update pending; JPEG sender is ACK-driven.
        Dispatcher.Invoke(() => image.Source = bitmap);
    }
}
