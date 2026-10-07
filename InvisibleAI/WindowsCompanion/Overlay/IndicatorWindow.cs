using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using InvisibleAI.Companion.Core;
using InvisibleAI.Companion.Settings;
using InvisibleAI.Companion.AI;

namespace InvisibleAI.Companion.Overlay;

public sealed class IndicatorWindow : Window
{
    private readonly DispatcherTimer expiry = new();
    private readonly Ellipse dot = new() { Fill = new SolidColorBrush(Color.FromRgb(106, 222, 185)) };
    private readonly TextBlock label = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
    private readonly Border response;
    private readonly StackPanel stack = new() { Orientation = Orientation.Horizontal };
    private nint handle;
    private bool interactive;
    private MonitorSnapshot? monitor;
    private AppSettings? settings;
    private AIAnswer? answer;
    public event Action<AIAnswer>? ExpandRequested;
    // Ordinary Window layout inherits Windows' minimum tracking size (roughly 136 DIP).
    // This borderless indicator measures only its visual content, so the HWND itself can be 6 DIP.
    protected override Size MeasureOverride(Size availableSize)
    {
        if (VisualChildrenCount == 0) return new Size(0, 0);
        var child = (UIElement)GetVisualChild(0);
        child.Measure(availableSize);
        return child.DesiredSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (VisualChildrenCount > 0) ((UIElement)GetVisualChild(0)).Arrange(new Rect(finalSize));
        return finalSize;
    }
    public IndicatorWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        response = new Border { Background = new SolidColorBrush(Color.FromRgb(25, 31, 40)),
            CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 6, 9, 6), Margin = new Thickness(0, 0, 6, 0), Child = label };
        dot.VerticalAlignment = VerticalAlignment.Bottom;
        stack.Children.Add(response); stack.Children.Add(dot);
        Content = stack;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(Hook);
            ApplyStyles();
        };
        SizeChanged += (_, _) => Position();
        expiry.Tick += (_, _) => HideIndicator();
        MouseLeftButtonUp += (_, _) => { if (interactive && answer is not null) ExpandRequested?.Invoke(answer); };
    }
    private void ApplyStyles()
    {
        if (handle == 0) return;
        long style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
        style = interactive ? style & ~NativeMethods.WsExTransparent : style | NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new nint(style));
    }
    private nint Hook(nint hwnd, int message, nint w, nint l, ref bool handled)
    {
        if (!interactive && message == NativeMethods.WmNcHitTest) { handled = true; return new(-1); }
        if (message == NativeMethods.WmMouseActivate) { handled = true; return new(3); }
        return 0;
    }
    private void Prepare(AppSettings value, MonitorSnapshot target, bool respond)
    {
        expiry.Stop(); settings = value; monitor = target;
        dot.BeginAnimation(OpacityProperty, null); dot.Width = dot.Height = value.DotSize;
        dot.Opacity = value.DotOpacity; interactive = respond && value.InteractiveResponse; ApplyStyles();
        response.Visibility = respond ? Visibility.Visible : Visibility.Collapsed;
        response.Opacity = value.ResponseOpacity; label.FontSize = value.ResponseFontSize;
        label.MaxWidth = value.ResponseWidth; label.MaxHeight = value.ResponseFontSize * 5;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        if (!NativeMethods.IsOrdinaryDesktop()) { HideIndicator(); return; }
        Show(); UpdateLayout(); Position();
    }
    public void ShowProcessing(AppSettings value, MonitorSnapshot target)
    {
        answer = null; label.Text = ""; Prepare(value, target, false);
        if (!IsVisible) return;
        dot.BeginAnimation(OpacityProperty, new DoubleAnimation(value.DotOpacity, 0.05,
            TimeSpan.FromMilliseconds(value.PulseMilliseconds / 2.0)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }
    public void ShowAnswer(AIAnswer value, AppSettings options, MonitorSnapshot target)
    {
        answer = value; label.Text = value.Text;
        // Always bounded; detailed content is available by an explicit expand action.
        if (label.Text.Length > 240) label.Text = label.Text[..239] + "…";
        Prepare(options, target, true);
        expiry.Interval = TimeSpan.FromSeconds(options.ResponseSeconds); expiry.Start();
    }
    public void HideIndicator() { expiry.Stop(); dot.BeginAnimation(OpacityProperty, null); Hide(); }
    private void Position()
    {
        if (handle == 0 || monitor is null || settings is null || !IsVisible) return;
        var workArea = MonitorService.CurrentWorkArea(monitor);
        // First move to the target monitor to let PMv2 update the window's DPI, then measure pixels.
        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, workArea.Right - 32, workArea.Bottom - 32, 0, 0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        double scale = Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96.0;
        int width = Math.Min(workArea.Width, (int)Math.Ceiling(ActualWidth * scale));
        int height = Math.Min(workArea.Height, (int)Math.Ceiling(ActualHeight * scale));
        var position = MonitorService.Anchor(workArea, width, height, (int)(settings.OffsetX * scale), (int)(settings.OffsetY * scale));
        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, position.X, position.Y, width, height, NativeMethods.SwpNoActivate);
    }
}
