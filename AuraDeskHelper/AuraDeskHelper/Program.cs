using System.IO.Pipes;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;

namespace AuraDeskHelper;

class Program
{
    // ─── Win32 Input ─────────────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] static extern IntPtr GetMessageExtraInfo();
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040;
    const uint MOUSEEVENTF_WHEEL = 0x0800;
    const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_SCANCODE = 0x0008;

    // ─── Win32 Screen ─────────────────────────────────────────────────────────
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, int dwRop);
    [DllImport("user32.dll")] static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    const int SRCCOPY = 0x00CC0020;

    // ─── Win32 Desktop ────────────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetProcessWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetThreadDesktop(IntPtr hDesktop);

    const uint WINSTA_ALL_ACCESS = 0x37F;
    const uint DESKTOP_GENERIC_ALL = 0x1FF;

    static void AttachToUserDesktop()
    {
        try
        {
            var winSta = OpenWindowStation("WinSta0", false, WINSTA_ALL_ACCESS);
            if (winSta != IntPtr.Zero)
            {
                SetProcessWindowStation(winSta);
                var desktop = OpenInputDesktop(0, false, DESKTOP_GENERIC_ALL);
                if (desktop != IntPtr.Zero)
                {
                    SetThreadDesktop(desktop);
                    Console.WriteLine("Attaché au bureau utilisateur");
                }
                else { Console.WriteLine("OpenInputDesktop échoué"); }
            }
            else { Console.WriteLine("OpenWindowStation échoué"); }
        }
        catch (Exception ex) { Console.WriteLine($"AttachToUserDesktop erreur: {ex.Message}"); }
    }

    static CancellationTokenSource _cts = new();
    static bool _previewEnabled = false;

    static async Task Main(string[] args)
    {
        Console.WriteLine("AuraDeskHelper démarré");
        AttachToUserDesktop();

        // Lancer la capture écran en parallèle
        var captureThread = new Thread(() => { AttachToUserDesktop(); ScreenCaptureLoop(_cts.Token).Wait(); });
        captureThread.IsBackground = true;
        captureThread.SetApartmentState(ApartmentState.STA);
        captureThread.Start();

        // Écouter les commandes input
        await InputPipeLoop(_cts.Token);
    }

    // ─── Screen capture → Service ─────────────────────────────────────────────
    static async Task ScreenCaptureLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_previewEnabled)
                {
                    var jpeg = CaptureScreen(35, 1280);
                    var b64  = Convert.ToBase64String(jpeg);
                    var json = JsonSerializer.Serialize(new { type = "screen_frame", data = b64 });

                    using var pipe = new NamedPipeClientStream(".", "AuraDeskScreenPipe", PipeDirection.Out);
                    await pipe.ConnectAsync(200, ct);
                    var data = System.Text.Encoding.UTF8.GetBytes(json + "\n");
                    await pipe.WriteAsync(data, ct);
                }
            }
            catch { }
            await Task.Delay(100, ct);
        }
    }

    static byte[] CaptureScreen(int quality, int maxWidth)
    {
        int sw = System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Width;
        int sh = System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Height;
        int tw = Math.Min(sw, maxWidth);
        int th = (int)(sh * (tw / (double)sw));

        IntPtr desktopWnd = GetDesktopWindow();
        IntPtr desktopDC  = GetDC(desktopWnd);
        IntPtr memDC      = CreateCompatibleDC(desktopDC);
        IntPtr bmp        = CreateCompatibleBitmap(desktopDC, sw, sh);
        IntPtr oldBmp     = SelectObject(memDC, bmp);
        BitBlt(memDC, 0, 0, sw, sh, desktopDC, 0, 0, SRCCOPY);
        SelectObject(memDC, oldBmp);

        using var fullBmp  = Image.FromHbitmap(bmp);
        using var thumbBmp = new Bitmap(tw, th);
        using var g        = Graphics.FromImage(thumbBmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
        g.DrawImage(fullBmp, 0, 0, tw, th);

        DeleteObject(bmp); DeleteDC(memDC); ReleaseDC(desktopWnd, desktopDC);

        GetCursorPos(out var cur);
        int cx = (int)(cur.X * (tw / (double)sw));
        int cy = (int)(cur.Y * (th / (double)sh));
        using var pen = new Pen(Color.Red, 2);
        g.DrawEllipse(pen, cx - 6, cy - 6, 12, 12);
        g.DrawLine(pen, cx - 10, cy, cx + 10, cy);
        g.DrawLine(pen, cx, cy - 10, cx, cy + 10);

        using var ms     = new MemoryStream();
        var encoder      = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        var encParams    = new EncoderParameters(1);
        encParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
        thumbBmp.Save(ms, encoder, encParams);
        return ms.ToArray();
    }

    // ─── Input pipe ───────────────────────────────────────────────────────────
    static async Task InputPipeLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream("AuraDeskInputPipe", PipeDirection.In, 1,
                    PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(pipe);
                while (pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break;
                    ProcessCommand(line);
                }
            }
            catch { await Task.Delay(1000, ct); }
        }
    }

    static void ProcessCommand(string json)
    {
        try
        {
            var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "set_preview": _previewEnabled = root.GetProperty("enabled").GetBoolean(); break;
                case "mouse_move":  SetCursorPos(root.GetProperty("x").GetInt32(), root.GetProperty("y").GetInt32()); break;
                case "mouse_click":
                    int x = root.GetProperty("x").GetInt32(), y = root.GetProperty("y").GetInt32();
                    var btn = root.TryGetProperty("button", out var b) ? b.GetString() ?? "left" : "left";
                    bool dbl = root.TryGetProperty("double", out var d) && d.GetBoolean();
                    SetCursorPos(x, y); Click(btn); if (dbl) { Thread.Sleep(50); Click(btn); }
                    break;
                case "mouse_scroll": Scroll(root.GetProperty("delta").GetInt32()); break;
                case "key_down":  KeyEvent(root.GetProperty("vk").GetUInt16(), false); break;
                case "key_up":    KeyEvent(root.GetProperty("vk").GetUInt16(), true); break;
                case "type_text": TypeText(root.GetProperty("text").GetString() ?? ""); break;
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






