using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AuraCtrlService.Screen;

public class ScreenCapture
{
    [DllImport("user32.dll")]
    static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int dwRop);

    [DllImport("user32.dll")]
    static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    private const int SRCCOPY = 0x00CC0020;

    public static byte[] CaptureScreen(int quality = 40, int maxWidth = 1280)
    {
        int screenW = System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Width;
        int screenH = System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Height;

        int thumbW = Math.Min(screenW, maxWidth);
        int thumbH = (int)(screenH * (thumbW / (double)screenW));

        IntPtr desktopWnd = GetDesktopWindow();
        IntPtr desktopDC  = GetDC(desktopWnd);
        IntPtr memDC      = CreateCompatibleDC(desktopDC);
        IntPtr bmp        = CreateCompatibleBitmap(desktopDC, screenW, screenH);
        IntPtr oldBmp     = SelectObject(memDC, bmp);

        BitBlt(memDC, 0, 0, screenW, screenH, desktopDC, 0, 0, SRCCOPY);
        SelectObject(memDC, oldBmp);

        using var fullBmp  = Image.FromHbitmap(bmp);
        using var thumbBmp = new Bitmap(thumbW, thumbH);
        using var g        = Graphics.FromImage(thumbBmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
        g.DrawImage(fullBmp, 0, 0, thumbW, thumbH);

        DeleteObject(bmp);
        DeleteDC(memDC);
        ReleaseDC(desktopWnd, desktopDC);

        GetCursorPos(out var cur);
        int cx = (int)(cur.X * (thumbW / (double)screenW));
        int cy = (int)(cur.Y * (thumbH / (double)screenH));
        using var pen = new Pen(Color.Red, 2);
        g.DrawEllipse(pen, cx - 6, cy - 6, 12, 12);
        g.DrawLine(pen, cx - 10, cy, cx + 10, cy);
        g.DrawLine(pen, cx, cy - 10, cx, cy + 10);

        using var ms     = new MemoryStream();
        var encoder      = GetJpegEncoder();
        var encParams    = new EncoderParameters(1);
        encParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
        thumbBmp.Save(ms, encoder, encParams);
        return ms.ToArray();
    }

    private static ImageCodecInfo GetJpegEncoder()
    {
        return ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
    }
}
