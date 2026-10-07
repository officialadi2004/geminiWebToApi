using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using InvisibleAI.Companion.Core;
using Forms = System.Windows.Forms;
using Rectangle = System.Drawing.Rectangle;
using Point = System.Drawing.Point;

namespace InvisibleAI.Companion.ScreenCapture;

public sealed class RegionCaptureService
{
    public InvisibleAI.Companion.Overlay.MonitorSnapshot? SelectedMonitor { get; private set; }
    public async Task<byte[]?> SelectAndCaptureAsync(CancellationToken ct)
    {
        if (!NativeMethods.IsOrdinaryDesktop()) throw new InvalidOperationException("Screen selection is unavailable on this Windows desktop.");
        var completion = new TaskCompletionSource<Rectangle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var windows = new List<SelectionWindow>();
        Point? start = null;
        Rectangle selection = Rectangle.Empty;
        try
        {
            foreach (var screen in Forms.Screen.AllScreens)
            {
                var window = new SelectionWindow(screen.Bounds);
                windows.Add(window);
                window.Begin += point => start = point;
                window.Move += point =>
                {
                    if (start is not Point origin) return;
                    selection = Rectangle.FromLTRB(Math.Min(origin.X, point.X), Math.Min(origin.Y, point.Y),
                        Math.Max(origin.X, point.X), Math.Max(origin.Y, point.Y));
                    foreach (var item in windows) item.Draw(selection);
                };
                window.Finish += () => completion.TrySetResult(selection.Width >= 4 && selection.Height >= 4 ? selection : null);
                window.Cancel += () => completion.TrySetResult(null);
            }
            using var registration = ct.Register(() => completion.TrySetCanceled(ct));
            foreach (var window in windows) window.Show();
            windows.FirstOrDefault(w => w.Bounds.Contains(Forms.Cursor.Position))?.Activate();
            var region = await completion.Task;
            foreach (var window in windows) window.Close();
            windows.Clear();
            if (region is null) return null;
            var target = Forms.Screen.FromPoint(new Point(region.Value.Left + region.Value.Width / 2, region.Value.Top + region.Value.Height / 2));
            SelectedMonitor = new(target.DeviceName, target.Bounds, target.WorkingArea);
            // Capture only after all selection windows are gone and compositor has caught up.
            await Task.Delay(120, ct);
            NativeMethods.DwmFlush();
            return await Task.Run(() => Capture(region.Value, ct), ct);
        }
        finally { foreach (var window in windows) window.Close(); }
    }
    public static byte[] Capture(Rectangle region, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!NativeMethods.IsOrdinaryDesktop()) throw new InvalidOperationException("Capture is unavailable on this Windows desktop.");
        if (region.Width < 4 || region.Height < 4 || (long)region.Width * region.Height > 16000000)
            throw new InvalidOperationException("Select a region between 4 pixels and 16 megapixels.");
        using var bitmap = new Bitmap(region.Width, region.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            try { graphics.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or ArgumentException)
            { throw new InvalidOperationException("Windows or this application did not allow screen capture."); }
        }
        ct.ThrowIfCancellationRequested();
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        if (memory.Length > 5 * 1024 * 1024) throw new InvalidOperationException("Select a smaller region (maximum PNG size 5 MiB).");
        return memory.ToArray();
    }
}

internal sealed class SelectionWindow : Window
{
    public Rectangle Bounds { get; }
    private readonly Canvas canvas = new();
    private readonly System.Windows.Shapes.Rectangle rectangle = new() { Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(106, 222, 185)), StrokeThickness = 2 };
    private nint handle;
    public event Action<Point>? Begin, Move;
    public event Action? Finish, Cancel;
    public SelectionWindow(Rectangle bounds)
    {
        Bounds = bounds; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(18, 0, 0, 0));
        ShowInTaskbar = false; Topmost = true; Cursor = System.Windows.Input.Cursors.Cross;
        Content = canvas; canvas.Children.Add(rectangle);
        var hint = new TextBlock { Text = "Drag to select · Esc to cancel", Foreground = System.Windows.Media.Brushes.White,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 31, 40)), Padding = new Thickness(8), FontSize = 12 };
        Canvas.SetLeft(hint, 18); Canvas.SetTop(hint, 18); canvas.Children.Add(hint);
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height, NativeMethods.SwpNoActivate);
        };
        MouseLeftButtonDown += (_, e) => { Begin?.Invoke(CursorPoint()); CaptureMouse(); e.Handled = true; };
        MouseMove += (_, _) => { if (IsMouseCaptured) Move?.Invoke(CursorPoint()); };
        MouseLeftButtonUp += (_, e) => { Move?.Invoke(CursorPoint()); ReleaseMouseCapture(); Finish?.Invoke(); e.Handled = true; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Cancel?.Invoke(); e.Handled = true; } };
        MouseRightButtonDown += (_, _) => Cancel?.Invoke();
        Deactivated += (_, _) => { if (IsMouseCaptured) Cancel?.Invoke(); };
        Closed += (_, _) => ReleaseMouseCapture();
    }
    private static Point CursorPoint() { NativeMethods.GetCursorPos(out var point); return new(point.X, point.Y); }
    public void Draw(Rectangle physical)
    {
        var clipped = Rectangle.Intersect(physical, Bounds);
        double dpi = Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96.0;
        rectangle.Width = Math.Max(0, clipped.Width / dpi); rectangle.Height = Math.Max(0, clipped.Height / dpi);
        Canvas.SetLeft(rectangle, (clipped.X - Bounds.X) / dpi); Canvas.SetTop(rectangle, (clipped.Y - Bounds.Y) / dpi);
    }
}
