using System.Drawing;
using Forms = System.Windows.Forms;
using InvisibleAI.Companion.Core;

namespace InvisibleAI.Companion.Overlay;

public sealed record MonitorSnapshot(string DeviceName, Rectangle Bounds, Rectangle WorkArea);
public static class MonitorService
{
    public static MonitorSnapshot Active(string preference = "Active")
    {
        Forms.Screen screen;
        if (preference == "Primary") screen = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        else if (preference != "Active") screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == preference) ?? WorkflowScreen();
        else screen = WorkflowScreen();
        return new(screen.DeviceName, screen.Bounds, screen.WorkingArea);
    }
    private static Forms.Screen WorkflowScreen()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != 0) return Forms.Screen.FromHandle(foreground);
        NativeMethods.GetCursorPos(out var p);
        return Forms.Screen.FromPoint(new Point(p.X, p.Y));
    }
    public static Rectangle CurrentWorkArea(MonitorSnapshot original) =>
        (Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == original.DeviceName) ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]).WorkingArea;
    public static Point Anchor(Rectangle area, int width, int height, int offsetX, int offsetY) => new(
        Math.Clamp(area.Right - width - offsetX, area.Left, Math.Max(area.Left, area.Right - width)),
        Math.Clamp(area.Bottom - height - offsetY, area.Top, Math.Max(area.Top, area.Bottom - height)));
}
