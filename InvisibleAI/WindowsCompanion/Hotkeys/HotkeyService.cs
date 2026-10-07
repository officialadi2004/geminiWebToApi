using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Interop;
using InvisibleAI.Companion.Core;

namespace InvisibleAI.Companion.Hotkeys;

public readonly record struct HotkeyGesture(uint Modifiers, uint KeyCode)
{
    public static HotkeyGesture Parse(string value)
    {
        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new ArgumentException("Use a modified shortcut, for example Ctrl+Shift+V.");
        uint mods = 0;
        foreach (var modifier in parts[..^1])
        {
            uint flag = modifier.ToLowerInvariant() switch { "ctrl" => 2u, "shift" => 4u, "alt" => 1u, "win" => 8u, _ => 0u };
            if (flag == 0 || (mods & flag) != 0) throw new ArgumentException("Invalid or repeated shortcut modifier.");
            mods |= flag;
        }
        if (mods == 4 || mods == 2) throw new ArgumentException("Use Ctrl+Shift, Alt, or Win to preserve ordinary application shortcuts.");
        if (!Enum.TryParse<Key>(parts[^1], true, out var key) || key == Key.None || KeyInterop.VirtualKeyFromKey(key) == 0)
            throw new ArgumentException("Invalid shortcut key.");
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            throw new ArgumentException("A shortcut must end with a non-modifier key.");
        return new(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
    }
}

public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource source;
    private readonly Dictionary<int, Action> actions = [];
    private List<(HotkeyGesture Gesture, Action Action)> registrations = [];
    private int nextId = 100;
    public HotkeyService()
    {
        source = new HwndSource(new HwndSourceParameters("InvisibleAI hotkeys") { ParentWindow = new nint(-3), Width = 0, Height = 0 });
        source.AddHook(Hook);
    }
    public void Configure(string[] shortcuts, Action[] callbacks)
    {
        var desired = shortcuts.Select((s, i) => (Gesture: HotkeyGesture.Parse(s), Action: callbacks[i])).ToList();
        var previous = registrations;
        Clear();
        try { Register(desired); registrations = desired; }
        catch
        {
            Clear();
            try { Register(previous); registrations = previous; } catch { Clear(); }
            throw;
        }
    }
    public string[] ConfigureAvailable(string[] shortcuts, Action[] callbacks)
    {
        Clear();
        var available = new List<(HotkeyGesture Gesture, Action Action)>();
        var conflicts = new List<string>();
        for (int i = 0; i < shortcuts.Length; i++)
        {
            var item = (Gesture: HotkeyGesture.Parse(shortcuts[i]), Action: callbacks[i]);
            try { Register([item]); available.Add(item); }
            catch (Win32Exception) { conflicts.Add(shortcuts[i]); }
        }
        registrations = available;
        return conflicts.ToArray();
    }
    private void Register(List<(HotkeyGesture Gesture, Action Action)> list)
    {
        foreach (var item in list)
        {
            int id = nextId++;
            if (!NativeMethods.RegisterHotKey(source.Handle, id, item.Gesture.Modifiers | 0x4000, item.Gesture.KeyCode))
                throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "A shortcut is already in use. Change it in Settings → Keyboard.");
            actions[id] = item.Action;
        }
    }
    private nint Hook(nint hwnd, int msg, nint w, nint l, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && actions.TryGetValue(w.ToInt32(), out var action)) { handled = true; action(); }
        return 0;
    }
    private void Clear() { foreach (int id in actions.Keys) NativeMethods.UnregisterHotKey(source.Handle, id); actions.Clear(); }
    public void Dispose() { Clear(); source.RemoveHook(Hook); source.Dispose(); }
}
