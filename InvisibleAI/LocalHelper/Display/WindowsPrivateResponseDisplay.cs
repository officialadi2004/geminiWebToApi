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
        private readonly List<Rectangle> copyBounds = [];
        private Rectangle viewport;
        private IntPtr browser;
        private int duration, scroll;
        private double remaining, last;
        private float scale = 1;
        private bool choice;
        private bool processing, expanded;
        public AnswerWindow()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            BackColor = Color.Magenta; TransparencyKey = BackColor; AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true; AccessibleName = "Invisible AI private response";
            timer.Tick += Tick;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80 | 0x80000 | Transparent; return p; } }
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
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) ||
                !Native.SetWindowDisplayAffinity(Handle, 0x11) ||
                !Native.GetWindowDisplayAffinity(Handle, out uint affinity) || affinity != 0x11)
                throw new AIProviderException("Private display unavailable. Windows capture exclusion is required.");
            request = id; browser = foreground; duration = settings.ResponseSeconds;
            processing = true; last = Seconds();
            Position(); Show(); timer.Start();
        }
        public void Answer(string id, AIAnswer answer)
        {
            if (request != id) return;
            summary = answer.Text; body = string.IsNullOrWhiteSpace(answer.Details) ? answer.Text : answer.Text + "\n" + answer.Details;
            choice = Regex.IsMatch(summary, @"\A(?:[A-Z]|[1-9][0-9]?)(?:, (?:[A-Z]|[1-9][0-9]?))*\z", RegexOptions.CultureInvariant);
            codes = Regex.Matches(body, @"```[^\r\n`]*\r?\n([\s\S]*?)```", RegexOptions.CultureInvariant)
                .Select(fence => Regex.Replace(fence.Groups[1].Value, @"\r?\n$", "", RegexOptions.CultureInvariant)).ToArray();
            processing = false; remaining = duration; last = Seconds(); scroll = 0;
            Position(); Invalidate();
        }
        public void HideAnswer(string? id)
        {
            if (id is not null && request != id) return;
            timer.Stop(); Hide(); request = null; summary = body = ""; codes = []; expanded = false;
            copyBounds.Clear(); scroll = 0; ClickThrough(true);
        }
        private void ClickThrough(bool value)
        {
            long style = Native.GetWindowLongPtr(Handle, -20).ToInt64();
            Native.SetWindowLongPtr(Handle, -20, new IntPtr(value ? style | Transparent : style & ~Transparent));
        }
        private static double Seconds() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        private void Tick(object? sender, EventArgs e)
        {
            if (request is null) return;
            // Poll only our transient bounds, rather than intercepting mouse input or hooks.
            bool hover = !processing && Bounds.Contains(MousePosition);
            double now = Seconds();
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
                int columns = Math.Max(1, width / (int)(62 * scale));
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
                using var background = new SolidBrush(BackColor);
                for (int i = 0; i < codes.Length; i++)
                {
                    int columns = Math.Max(1, ClientSize.Width / (int)(62 * scale));
                    var bounds = new Rectangle((i % columns) * (int)(62 * scale), ClientSize.Height - ((i / columns) + 1) * (int)(22 * scale), (int)(62 * scale), (int)(22 * scale));
                    copyBounds.Add(bounds); e.Graphics.FillRectangle(background, bounds);
                    e.Graphics.DrawString(codes.Length == 1 ? "Copy" : "Copy " + (i + 1), font, ink, bounds);
                }
            }
        }
        protected override void OnMouseWheel(MouseEventArgs e) { if (expanded) { scroll = Math.Clamp(scroll - Math.Sign(e.Delta) * (int)(36 * scale), 0, 16000); Invalidate(); } }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !expanded) return;
            int index = copyBounds.FindIndex(bounds => bounds.Contains(e.Location));
            if (index >= 0 && index < codes.Length)
                try { System.Windows.Forms.Clipboard.SetText(codes[index], TextDataFormat.UnicodeText); } catch (ExternalException) { /* Clipboard locked; leave the original intact. */ }
        }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); summary = body = ""; codes = []; } base.Dispose(disposing); }
    }
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
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
