using System.Runtime.InteropServices;

namespace AuraCtrlService.Input;

public static class InputInjector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx; public int dy; public uint mouseData;
        public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk; public ushort wScan;
        public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }

    private const uint INPUT_MOUSE    = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP     = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;
    private const uint MOUSEEVENTF_WHEEL      = 0x0800;
    private const uint KEYEVENTF_KEYUP        = 0x0002;
    private const uint KEYEVENTF_SCANCODE     = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetMessageExtraInfo();

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    public static void MoveMouse(int x, int y) => SetCursorPos(x, y);

    public static void MouseClick(int x, int y, string button = "left", bool doubleClick = false)
    {
        SetCursorPos(x, y);
        uint down, up;
        switch (button)
        {
            case "right":  down = MOUSEEVENTF_RIGHTDOWN;  up = MOUSEEVENTF_RIGHTUP;  break;
            case "middle": down = MOUSEEVENTF_MIDDLEDOWN; up = MOUSEEVENTF_MIDDLEUP; break;
            default:       down = MOUSEEVENTF_LEFTDOWN;   up = MOUSEEVENTF_LEFTUP;   break;
        }
        SendMouseEvent(down); SendMouseEvent(up);
        if (doubleClick) { Thread.Sleep(50); SendMouseEvent(down); SendMouseEvent(up); }
    }

    public static void MouseScroll(int delta)
    {
        var inputs = new INPUT[] { new INPUT { type = INPUT_MOUSE, u = new InputUnion {
            mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_WHEEL, mouseData = (uint)(delta * 120),
            dwExtraInfo = GetMessageExtraInfo() } } } };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void KeyPress(ushort vk)
    {
        var inputs = new INPUT[] {
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwExtraInfo = GetMessageExtraInfo() } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP, dwExtraInfo = GetMessageExtraInfo() } } }
        };
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void KeyDown(ushort vk)
    {
        var inputs = new INPUT[] { new INPUT { type = INPUT_KEYBOARD, u = new InputUnion {
            ki = new KEYBDINPUT { wVk = vk, dwExtraInfo = GetMessageExtraInfo() } } } };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void KeyUp(ushort vk)
    {
        var inputs = new INPUT[] { new INPUT { type = INPUT_KEYBOARD, u = new InputUnion {
            ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP, dwExtraInfo = GetMessageExtraInfo() } } } };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void TypeText(string text)
    {
        var inputs = new List<INPUT>();
        foreach (char c in text)
        {
            ushort sc = (ushort)c;
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = sc, dwFlags = KEYEVENTF_SCANCODE, dwExtraInfo = GetMessageExtraInfo() } } });
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = sc, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, dwExtraInfo = GetMessageExtraInfo() } } });
        }
        if (inputs.Count > 0) SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static void SendMouseEvent(uint flags)
    {
        var inputs = new INPUT[] { new INPUT { type = INPUT_MOUSE, u = new InputUnion {
            mi = new MOUSEINPUT { dwFlags = flags, dwExtraInfo = GetMessageExtraInfo() } } } };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }
}
