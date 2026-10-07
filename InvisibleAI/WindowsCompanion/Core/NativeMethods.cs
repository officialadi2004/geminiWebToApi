using System.Runtime.InteropServices;

namespace InvisibleAI.Companion.Core;

internal static class NativeMethods
{
    public const int GwlExStyle = -20, WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
    public const int WmHotkey = 0x0312, WmNcHitTest = 0x84, WmMouseActivate = 0x21;
    public const uint SwpNoActivate = 0x10, SwpNoSize = 1, SwpNoMove = 2;
    public static readonly nint HwndTopmost = new(-1);
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetUserObjectInformation(nint handle, int index, System.Text.StringBuilder value, int length, out int needed);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool CloseDesktop(nint handle);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();

    public static bool IsOrdinaryDesktop()
    {
        nint desktop = OpenInputDesktop(0, false, 1);
        if (desktop == 0) return false;
        try
        {
            var name = new System.Text.StringBuilder(256);
            return GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out _) &&
                string.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
        }
        finally { CloseDesktop(desktop); }
    }
}
