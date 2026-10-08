using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using InvisibleAI.Helper.AI;
using InvisibleAI.Helper.Settings;

namespace InvisibleAI.Helper.Display;

// A single optional, transient HWND in the existing native host. No tray, hooks,
// screen monitoring or capture service. The window is never shown without affinity.
public sealed class WindowsPrivateResponseDisplay : IPrivateResponseDisplay
{
    private readonly object gate = new();
    private Task<AnswerWindow>? ready;
    private bool disposed;
    private Task<AnswerWindow> Window()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (ready is not null) return ready;
            var source = new TaskCompletionSource<AnswerWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            ready = source.Task;
            var thread = new Thread(() =>
            {
                Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
                try
                {
                    // Never let WinForms expose an exception in a separate unprotected dialog.
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, true);
                    using var window = new AnswerWindow();
                    _ = window.Handle; // Hidden until Begin has verified capture exclusion.
                    window.BeginInvoke(() => source.TrySetResult(window));
                    Application.Run();
                }
                catch { source.TrySetException(new AIProviderException("Private display unavailable on this Windows desktop.")); }
            }) { IsBackground = true, Name = "InvisibleAI private display" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            return ready;
        }
    }
    private static async Task OnUI(Task<AnswerWindow> ready, Action<AnswerWindow> action, CancellationToken ct = default)
    {
        var window = await ready.WaitAsync(ct);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try { window.BeginInvoke(() => { try { ct.ThrowIfCancellationRequested(); action(window); done.TrySetResult(); } catch (Exception e) { done.TrySetException(e); } }); }
        catch { throw new AIProviderException("Private display unavailable on this Windows desktop."); }
        await done.Task;
    }
    public Task BeginAsync(string id, AppSettings settings, CancellationToken ct)
    {
        // Resolve the browser's viewport before switching to our own UI thread.
        var foreground = Native.GetForegroundWindow();
        return OnUI(Window(), w => w.Begin(id, settings, foreground), ct);
    }
    public Task AnswerAsync(string id, AIAnswer answer, CancellationToken ct) => OnUI(Window(), w => w.Answer(id, answer), ct);
    public Task HideAsync(string? id = null)
    {
        lock (gate) return ready is null || disposed ? Task.CompletedTask : OnUI(ready, w => w.HideAnswer(id));
    }
    public void Dispose()
    {
        Task<AnswerWindow>? value;
        lock (gate) { if (disposed) return; disposed = true; value = ready; }
        if (value is not null) try { OnUI(value, w => { w.HideAnswer(null); w.Close(); Application.ExitThread(); }).Wait(TimeSpan.FromSeconds(3)); } catch { /* Host exit still destroys its HWND. */ }
    }
    internal Task InspectAsync(Action<IntPtr, Rectangle, bool> inspect) => OnUI(Window(), w => inspect(w.Handle, w.Bounds, w.Visible));

    private sealed class AnswerWindow : Form
    {
        private const int Transparent = 0x20;
        private readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
        private string? request;
        private string summary = "", body = "";
        private string[] codes = [];
        private double[] copiedUntil = [];
        private int failedCopy = -1;
        private bool copying;
        private readonly List<Rectangle> copyBounds = [];
        private Rectangle viewport;
        private IntPtr browser;
        private int duration, scroll;
        private double remaining, last;
        private float scale = 1;
        private bool choice;
        private bool processing, expanded;
        private bool captureReady;
        private double lastProtectionCheck;
        public AnswerWindow()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta; TransparencyKey = BackColor; AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true; AccessibleName = "Invisible AI private response";
            timer.Tick += Tick;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.Style &= ~0x10000000; // WS_VISIBLE: all new/recreated HWNDs start hidden.
                p.ExStyle |= 0x08000000 | 0x80 | 0x80000 | Transparent;
                return p;
            }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            // Apply before base handlers or a subsequent ShowWindow can reveal any content.
            captureReady = ApplyCaptureExclusion();
            base.OnHandleCreated(e);
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            captureReady = false;
            base.OnHandleDestroyed(e);
        }
        protected override void SetVisibleCore(bool value)
        {
            if (value)
            {
                _ = Handle;
                captureReady = ApplyCaptureExclusion();
                if (!captureReady) throw new AIProviderException("Private display unavailable. Windows capture exclusion is required.");
            }
            base.SetVisibleCore(value);
            if (value) Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x213); // No activation or owner changes.
        }
        private bool ApplyCaptureExclusion() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
            Native.DwmIsCompositionEnabled(out bool composing) == 0 && composing &&
            Native.SetWindowDisplayAffinity(Handle, 0x11) &&
            Native.GetWindowDisplayAffinity(Handle, out uint affinity) && affinity == 0x11;
        private bool CaptureExclusionIntact() => IsHandleCreated &&
            Native.DwmIsCompositionEnabled(out bool composing) == 0 && composing &&
            Native.GetWindowDisplayAffinity(Handle, out uint affinity) && affinity == 0x11;
        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            base.WndProc(ref m);
        }
        public void Begin(string id, AppSettings settings, IntPtr foreground)
        {
            HideAnswer(null);
            // Verify while hidden. Never downgrade to WDA_MONITOR or an unprotected surface.
            Opacity = settings.ResponseOpacity;
            captureReady = ApplyCaptureExclusion();
            if (!captureReady)
                throw new AIProviderException("Private display unavailable. Windows capture exclusion is required.");
            request = id; browser = foreground; duration = settings.ResponseSeconds;
            processing = true; last = Seconds(); lastProtectionCheck = last;
            Position(); Show(); timer.Start();
        }
        public void Answer(string id, AIAnswer answer)
        {
            if (request != id) return;
            captureReady = CaptureExclusionIntact();
            if (!captureReady) { HideAnswer(id); throw new AIProviderException("Private display unavailable. Windows capture exclusion was lost."); }
            summary = answer.Text; body = string.IsNullOrWhiteSpace(answer.Details) ? answer.Text : answer.Text + "\n" + answer.Details;
            choice = Regex.IsMatch(summary, @"\A(?:[A-Z]|[1-9][0-9]?)(?:, (?:[A-Z]|[1-9][0-9]?))*\z", RegexOptions.CultureInvariant);
            codes = Regex.Matches(body, @"```[^\r\n`]*\r?\n([\s\S]*?)```", RegexOptions.CultureInvariant)
                .Select(fence => Regex.Replace(fence.Groups[1].Value, @"\r?\n$", "", RegexOptions.CultureInvariant)).ToArray();
            copiedUntil = new double[codes.Length]; failedCopy = -1;
            processing = false; remaining = duration; last = Seconds(); scroll = 0;
            Position(); Invalidate();
        }
        public void HideAnswer(string? id)
        {
            if (id is not null && request != id) return;
            timer.Stop(); Hide(); request = null; summary = body = ""; codes = []; expanded = false;
            copyBounds.Clear(); copiedUntil = []; failedCopy = -1; scroll = 0; ClickThrough(true);
        }
        private void ClickThrough(bool value)
        {
            long style = Native.GetWindowLongPtr(Handle, -20).ToInt64();
            Native.SetWindowLongPtr(Handle, -20, new IntPtr(value ? style | Transparent : style & ~Transparent));
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x237); // Apply styles without focus/Z-order changes.
        }
        private static double Seconds() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        private void Tick(object? sender, EventArgs e)
        {
            if (request is null) return;
            // Poll only our transient bounds, rather than intercepting mouse input or hooks.
            bool hover = !processing && Bounds.Contains(MousePosition);
            double now = Seconds();
            if (now - lastProtectionCheck >= .25)
            {
                lastProtectionCheck = now; captureReady = CaptureExclusionIntact();
                if (!captureReady) { HideAnswer(null); return; }
                // Restore lost topmost status only during this browser workflow; do not fight other apps.
                if (Native.GetForegroundWindow() == browser && (Native.GetWindowLongPtr(Handle, -20).ToInt64() & 8) == 0)
                    Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x213);
            }
            if (!processing && !expanded && !hover) remaining -= now - last;
            last = now;
            if (!processing && remaining <= 0) { HideAnswer(null); return; }
            if (expanded != hover) { expanded = hover; scroll = 0; ClickThrough(!expanded); Position(); }
            else Position(); // Tracks only viewport geometry while a response is visible.
            Invalidate();
        }
        private void Position()
        {
            var screen = Screen.FromHandle(browser);
            viewport = screen.Bounds;
            if (Native.IsWindowVisible(browser) && Native.GetClientRect(browser, out var rect))
            {
                var origin = new Native.Point(); Native.ClientToScreen(browser, ref origin);
                var client = Rectangle.FromLTRB(origin.X, origin.Y, origin.X + rect.Right, origin.Y + rect.Bottom);
                if (client.Width > 100 && client.Height > 100) viewport = Rectangle.Intersect(viewport, client);
            }
            scale = Math.Max(1, Native.GetDpiForWindow(browser)) / 96f;
            int inset = (int)(20 * scale);
            int width = processing ? (int)(8 * scale) : expanded ? (int)(460 * scale) : (int)(180 * scale);
            int height = processing ? (int)(8 * scale) : expanded ? (int)(320 * scale) : (int)(20 * scale);
            if (!processing && !expanded)
            {
                using var font = FontFor(10);
                width = Math.Min(width, TextRenderer.MeasureText(Preview(), font).Width + (int)(8 * scale));
            }
            else if (!processing)
            {
                using var font = FontFor(choice && body == summary ? 14 : 12);
                using var graphics = CreateGraphics();
                var measured = graphics.MeasureString(body, font, Math.Max(1, Math.Min(width, viewport.Width - inset * 2)));
                width = Math.Min(width, (int)Math.Ceiling(measured.Width) + (int)(8 * scale));
                if (codes.Length != 0) width = Math.Max(width, (int)(88 * scale));
                int columns = Math.Max(1, width / (int)(88 * scale));
                int copyHeight = ((codes.Length + columns - 1) / columns) * (int)(22 * scale);
                height = Math.Min((int)(420 * scale), (int)Math.Ceiling(measured.Height) + (int)(8 * scale) + copyHeight);
            }
            width = Math.Clamp(width, 1, Math.Max(1, viewport.Width - inset * 2));
            height = Math.Clamp(height, 1, Math.Max(1, viewport.Height - inset * 2));
            Bounds = new Rectangle(Math.Max(viewport.Left, viewport.Right - inset - width), Math.Max(viewport.Top, viewport.Bottom - inset - height), width, height);
        }
        private string Preview() => codes.Length != 0 ? "Code" : summary.Length > 40 ? summary[..39] + "…" : summary.Replace('\n', ' ');
        private Font FontFor(float pixels) => new(codes.Length == 0 ? "Segoe UI" : "Consolas", pixels * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!captureReady) return;
            // Avoid blending antialiased text against the transparent magenta color key.
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            using var ink = new SolidBrush(Color.FromArgb(114, 121, 133));
            if (processing)
            {
                if ((int)(Seconds() * 2) % 2 == 0) e.Graphics.FillEllipse(ink, 0, 0, 6 * scale, 6 * scale);
                return;
            }
            using var font = FontFor(expanded ? choice && body == summary ? 14 : 12 : 10);
            e.Graphics.DrawString(expanded ? body : Preview(), font, ink,
                new RectangleF(0, expanded ? -scroll : 0, ClientSize.Width, expanded ? 20000 : ClientSize.Height));
            copyBounds.Clear();
            if (expanded && codes.Length != 0)
            {
                // Color-key pixels pass mouse clicks through even without WS_EX_TRANSPARENT.
                // Back only these tiny controls, leaving the surrounding answer transparent.
                using var background = new SolidBrush(Color.FromArgb(239, 241, 244));
                using var icon = new Pen(ink.Color, Math.Max(1, scale));
                for (int i = 0; i < codes.Length; i++)
                {
                    int columns = Math.Max(1, ClientSize.Width / (int)(88 * scale));
                    var bounds = new Rectangle((i % columns) * (int)(88 * scale), ClientSize.Height - ((i / columns) + 1) * (int)(22 * scale), Math.Min(ClientSize.Width, (int)(88 * scale)), (int)(22 * scale));
                    copyBounds.Add(bounds); e.Graphics.FillRectangle(background, bounds);
                    bool copied = copiedUntil[i] > Seconds();
                    float x = bounds.X + 4 * scale, y = bounds.Y + 5 * scale;
                    if (copied) e.Graphics.DrawLines(icon, [new PointF(x, y + 5 * scale), new PointF(x + 3 * scale, y + 8 * scale), new PointF(x + 10 * scale, y + scale)]);
                    else { e.Graphics.DrawRectangle(icon, x, y, 7 * scale, 9 * scale); e.Graphics.DrawRectangle(icon, x + 3 * scale, y + 3 * scale, 7 * scale, 9 * scale); }
                    string label = failedCopy == i ? "Retry" : copied ? "Copied" : "Copy";
                    if (codes.Length > 1) label += " " + (i + 1);
                    using var labelFont = new Font("Segoe UI", 10 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                    e.Graphics.DrawString(label, labelFont, ink, new RectangleF(bounds.X + 19 * scale, bounds.Y + 4 * scale, bounds.Width - 19 * scale, bounds.Height));
                }
            }
        }
        protected override void OnMouseWheel(MouseEventArgs e) { if (expanded) { scroll = Math.Clamp(scroll - Math.Sign(e.Delta) * (int)(36 * scale), 0, 16000); Invalidate(); } }
        protected override async void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !expanded || copying) return;
            int index = copyBounds.FindIndex(bounds => bounds.Contains(e.Location));
            if (index < 0 || index >= codes.Length) return;
            string? owner = request;
            var data = new DataObject(); data.SetData(DataFormats.UnicodeText, codes[index]);
            copying = true; copiedUntil[index] = 0; failedCopy = -1; Invalidate();
            try
            {
                for (int attempt = 0; attempt < 5 && request == owner && !IsDisposed; attempt++)
                {
                    try
                    {
                        // No blocking clipboard retry loop: keep hover/display protection responsive.
                        System.Windows.Forms.Clipboard.SetDataObject(data, true, 0, 0);
                        copiedUntil[index] = Seconds() + 1.8; failedCopy = -1; Invalidate(); return;
                    }
                    catch (ExternalException) { if (attempt < 4) await Task.Delay(40); }
                }
                if (request == owner && !IsDisposed) { failedCopy = index; Invalidate(); }
            }
            finally { copying = false; }
        }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); summary = body = ""; codes = []; } base.Dispose(disposing); }
    }
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
        [DllImport("dwmapi.dll")] internal static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
        [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
