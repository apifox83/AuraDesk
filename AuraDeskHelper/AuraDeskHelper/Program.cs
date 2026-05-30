using System.IO.Pipes;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace AuraDeskHelper;

class Program
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    static extern IntPtr GetMessageExtraInfo();

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    const uint INPUT_MOUSE    = 0;
    const uint INPUT_KEYBOARD = 1;
    const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
    const uint MOUSEEVENTF_LEFTUP     = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
    const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;
    const uint MOUSEEVENTF_WHEEL      = 0x0800;
    const uint KEYEVENTF_KEYUP        = 0x0002;
    const uint KEYEVENTF_SCANCODE     = 0x0008;

    static async Task Main(string[] args)
    {
        Console.WriteLine("AuraDeskHelper démarré — en attente de commandes...");

        while (true)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    "AuraDeskInputPipe",
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync();

                using var reader = new StreamReader(pipe);
                while (pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;
                    ProcessCommand(line);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pipe error: {ex.Message}");
                await Task.Delay(1000);
            }
        }
    }

    static void ProcessCommand(string json)
    {
        try
        {
            var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "mouse_move":
                    SetCursorPos(root.GetProperty("x").GetInt32(), root.GetProperty("y").GetInt32());
                    break;

                case "mouse_click":
                    int x   = root.GetProperty("x").GetInt32();
                    int y   = root.GetProperty("y").GetInt32();
                    var btn = root.TryGetProperty("button", out var b) ? b.GetString() ?? "left" : "left";
                    bool dbl = root.TryGetProperty("double", out var d) && d.GetBoolean();
                    SetCursorPos(x, y);
                    Click(btn); if (dbl) { Thread.Sleep(50); Click(btn); }
                    break;

                case "mouse_scroll":
                    Scroll(root.GetProperty("delta").GetInt32());
                    break;

                case "key_down":
                    KeyEvent(root.GetProperty("vk").GetUInt16(), false);
                    break;

                case "key_up":
                    KeyEvent(root.GetProperty("vk").GetUInt16(), true);
                    break;

                case "type_text":
                    TypeText(root.GetProperty("text").GetString() ?? "");
                    break;
            }
        }
        catch (Exception ex) { Console.WriteLine($"Command error: {ex.Message}"); }
    }

    static void Click(string btn)
    {
        uint down, up;
        switch (btn)
        {
            case "right":  down = MOUSEEVENTF_RIGHTDOWN;  up = MOUSEEVENTF_RIGHTUP;  break;
            case "middle": down = MOUSEEVENTF_MIDDLEDOWN; up = MOUSEEVENTF_MIDDLEUP; break;
            default:       down = MOUSEEVENTF_LEFTDOWN;   up = MOUSEEVENTF_LEFTUP;   break;
        }
        SendMouse(down); SendMouse(up);
    }

    static void Scroll(int delta)
    {
        var inputs = new INPUT[] { new INPUT { type = INPUT_MOUSE, u = new InputUnion {
            mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_WHEEL, mouseData = (uint)(delta * 120),
            dwExtraInfo = GetMessageExtraInfo() } } } };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    static void KeyEvent(ushort vk, bool keyUp)
    {
        var inputs = new INPUT[] { new INPUT { type = INPUT_KEYBOARD, u = new InputUnion {
            ki = new KEYBDINPUT { wVk = vk, dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
            dwExtraInfo = GetMessageExtraInfo() } } } };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    static void TypeText(string text)
    {
        var inputs = new List<INPUT>();
        foreach (char c in text)
        {
            ushort sc = (ushort)c;
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT {
                wScan = sc, dwFlags = KEYEVENTF_SCANCODE, dwExtraInfo = GetMessageExtraInfo() } } });
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT {
                wScan = sc, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, dwExtraInfo = GetMessageExtraInfo() } } });
        }
        if (inputs.Count > 0) SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    static void SendMouse(uint flags)
    {
        var inputs = new INPUT[] { new INPUT { type = INPUT_MOUSE, u = new InputUnion {
            mi = new MOUSEINPUT { dwFlags = flags, dwExtraInfo = GetMessageExtraInfo() } } } };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }
}
