using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
namespace VoxDeck.Platform;

public static class Keys
{
    static readonly (Key key, string name, uint vk)[] Table = Build();
    static (Key, string, uint)[] Build()
    {
        var l = new List<(Key, string, uint)>();
        for (int i = 0; i < 26; i++) l.Add((Key.A + i, ((char)('A' + i)).ToString(), (uint)(0x41 + i)));
        for (int i = 0; i < 10; i++) { l.Add((Key.D0 + i, i.ToString(), (uint)(0x30 + i))); l.Add((Key.NumPad0 + i, "Num" + i, (uint)(0x60 + i))); }
        for (int i = 0; i < 24; i++) l.Add((Key.F1 + i, "F" + (i + 1), (uint)(0x70 + i)));
        l.AddRange(new (Key, string, uint)[]
        {
            (Key.Space, "Space", 0x20), (Key.Tab, "Tab", 0x09), (Key.Home, "Home", 0x24), (Key.End, "End", 0x23), (Key.PageUp, "PgUp", 0x21), (Key.PageDown, "PgDn", 0x22),
            (Key.Insert, "Ins", 0x2D), (Key.Delete, "Del", 0x2E), (Key.Up, "Up", 0x26), (Key.Down, "Down", 0x28), (Key.Left, "Left", 0x25), (Key.Right, "Right", 0x27),
            (Key.OemMinus, "-", 0xBD), (Key.OemPlus, "=", 0xBB), (Key.OemOpenBrackets, "[", 0xDB), (Key.OemCloseBrackets, "]", 0xDD), (Key.OemSemicolon, ";", 0xBA), (Key.OemQuotes, "'", 0xDE),
            (Key.OemComma, ",", 0xBC), (Key.OemPeriod, ".", 0xBE), (Key.OemQuestion, "/", 0xBF), (Key.OemPipe, "\\", 0xDC), (Key.OemTilde, "`", 0xC0),
            (Key.Multiply, "Num*", 0x6A), (Key.Add, "Num+", 0x6B), (Key.Subtract, "Num-", 0x6D), (Key.Divide, "Num/", 0x6F), (Key.Decimal, "Num.", 0x6E), (Key.Pause, "Pause", 0x13), (Key.Scroll, "ScrLk", 0x91),
        });
        return l.ToArray();
    }
    /// <summary>Builds "Ctrl+Alt+K" from a key event, or null when only modifiers are held.</summary>
    public static string Combo(KeyEventArgs e)
    {
        var t = Table.FirstOrDefault(x => x.key == e.Key); if (t.name == null) return null;
        var parts = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        parts.Add(t.name);
        return string.Join("+", parts);
    }
    public static bool TryParse(string combo, out uint mods, out uint vk)
    {
        mods = 0; vk = 0; if (string.IsNullOrWhiteSpace(combo)) return false;
        var tokens = combo.Split('+').Select(x => x.Trim()).ToList();
        if (tokens.Count >= 2 && tokens[^1] == "") { tokens[^2] += "+"; tokens.RemoveAt(tokens.Count - 1); }   // "Ctrl+Num+"
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= 2; break;
                case "alt": mods |= 1; break;
                case "shift": mods |= 4; break;
                case "win": case "meta": mods |= 8; break;
                default: return false;
            }
        }
        var t = Table.FirstOrDefault(x => string.Equals(x.name, tokens[^1], StringComparison.OrdinalIgnoreCase));
        if (t.name == null) return false;
        vk = t.vk; return true;
    }
}

/// <summary>System-wide hotkeys (Windows only; silently inactive elsewhere).</summary>
public sealed class HotkeyManager
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    const uint WM_HOTKEY = 0x0312, MOD_NOREPEAT = 0x4000;
    IntPtr hwnd; readonly Dictionary<int, Action> actions = new(); int nextId = 0x5000; bool hooked;

    public void Attach(Window w)
    {
        if (!OperatingSystem.IsWindows() || hooked) return;
        try
        {
            hwnd = w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero; if (hwnd == IntPtr.Zero) return;
            Win32Properties.AddWndProcHookCallback(w, Hook); hooked = true;
        }
        catch { }
    }
    IntPtr Hook(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && actions.TryGetValue((int)wParam, out var a)) { try { a(); } catch { } handled = true; }
        return IntPtr.Zero;
    }
    public void Clear()
    {
        if (!hooked) return;
        foreach (var id in actions.Keys) { try { UnregisterHotKey(hwnd, id); } catch { } }
        actions.Clear();
    }
    public bool Register(string combo, Action a)
    {
        if (!hooked || !Keys.TryParse(combo, out var mods, out var vk)) return false;
        int id = nextId++;
        try { if (!RegisterHotKey(hwnd, id, mods | MOD_NOREPEAT, vk)) return false; } catch { return false; }
        actions[id] = a; return true;
    }
}

public static class WinStyles
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    const int GWL_EXSTYLE = -20; const long WS_EX_NOACTIVATE = 0x08000000L, WS_EX_TOOLWINDOW = 0x00000080L;
    /// <summary>Makes an overlay window not steal keyboard focus from the game / chat it floats above.</summary>
    public static void SetNoActivate(Window w, bool on)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var h = w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero; if (h == IntPtr.Zero) return;
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            ex = on ? ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW : ex & ~WS_EX_NOACTIVATE;
            SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
        }
        catch { }
    }
}
