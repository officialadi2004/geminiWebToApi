using System;
using System.Reflection;
using System.Drawing;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using InvisibleAI.Helper.Display;

namespace InvisibleAI.Tests;

// Test executable only, never shipped. Commands can reveal ONLY a fixed synthetic
// answer to establish a capture positive control. No provider, credentials or real input.
internal static class NativeCaptureFixture
{
    public static async Task<int> RunAsync()
    {
        using var display = new WindowsPrivateResponseDisplay();
        TestSurface? background = null;
        bool started = false;
        try
        {
            while (await Console.In.ReadLineAsync() is string command)
            {
                if (command == "quit") return 0;
                if (command is not ("protected" or "unprotected" or "hide")) return 1;
                if (!started)
                {
                    await display.BeginAsync("capture-fixture", new() { ResponseOpacity = 1, ResponseSeconds = 120 }, default);
                    await display.AnswerAsync("capture-fixture", new("SYNTHETIC ANSWER", []), default);
                    started = true;
                }
                if (command == "hide") await display.HideAsync();
                await display.InspectAsync((window, bounds, visible) =>
                {
                    // Freeze the UI timer only in this synthetic fixture so positive-control
                    // frames can reach the browser encoder before restoring exclusion.
                    var control = Control.FromHandle(window)!;
                    if (background is null)
                    {
                        background = new TestSurface { Bounds = Rectangle.Inflate(bounds, 12, 12) };
                        background.Show(); background.Refresh();
                        WindowsPrivateResponseDisplay.Native.SetWindowPos(background.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x213);
                        WindowsPrivateResponseDisplay.Native.SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x213);
                    }
                    var timer = (Timer)control.GetType().GetField("timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(control)!;
                    timer.Stop();
                    uint requested = command == "unprotected" ? 0u : 0x11u;
                    if (!WindowsPrivateResponseDisplay.Native.SetWindowDisplayAffinity(window, requested) ||
                        !WindowsPrivateResponseDisplay.Native.GetWindowDisplayAffinity(window, out uint affinity) || affinity != requested)
                        throw new InvalidOperationException("Synthetic capture fixture unavailable.");
                    var monitor = Screen.FromHandle(window).Bounds;
                    Console.WriteLine(JsonSerializer.Serialize(new { visible, affinity, screens = Screen.AllScreens.Length,
                        background = new { visible = background.Visible, x = background.Bounds.X, y = background.Bounds.Y, width = background.Bounds.Width, height = background.Bounds.Height },
                        crop = new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height },
                        monitor = new { x = monitor.X, y = monitor.Y, width = monitor.Width, height = monitor.Height } }));
                });
            }
            return 0;
        }
        catch { Console.WriteLine("{\"error\":\"Synthetic capture fixture unavailable.\"}"); return 1; }
        finally { await display.InspectAsync((_, _, _) => { background?.Close(); background?.Dispose(); }); }
    }
    private sealed class TestSurface : Form
    {
        public TestSurface() { FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; BackColor = Color.White; AutoScaleMode = AutoScaleMode.None; StartPosition = FormStartPosition.Manual; }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000080; return p; } }
    }
}
