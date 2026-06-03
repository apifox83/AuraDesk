using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AuraDeskService
{
    public class ScreenInfo
    {
        public string Id       { get; set; } = "";   // ex: "\\.\DISPLAY1"
        public string Name     { get; set; } = "";   // ex: "DISPLAY1"
        public int    Width    { get; set; }
        public int    Height   { get; set; }
        public int    Hz       { get; set; }
        public List<DisplayMode> Modes { get; set; } = new();
    }

    public class DisplayMode
    {
        public int W  { get; set; }
        public int H  { get; set; }
        public int Hz { get; set; }
    }

    public static class DisplayManager
    {
        // â”€â”€ Win32 P/Invoke â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public uint   dmFields;
            public int    dmPositionX, dmPositionY;
            public uint   dmDisplayOrientation, dmDisplayFixedOutput;
            public short  dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short  dmLogPixels;
            public uint   dmBitsPerPel;
            public uint   dmPelsWidth, dmPelsHeight;
            public uint   dmDisplayFlags;
            public uint   dmDisplayFrequency;
            public uint   dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
            public uint   dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DISPLAY_DEVICE
        {
            public uint   cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public uint   StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        private const int  ENUM_CURRENT_SETTINGS = -1;
        private const uint CDS_UPDATEREGISTRY    = 0x01;
        private const uint CDS_TEST              = 0x02;
        private const uint CDS_FULLSCREEN        = 0x04;  // temporaire (sans registry)
        private const int  DISP_CHANGE_SUCCESSFUL = 0;
        private const uint DISPLAY_DEVICE_ACTIVE  = 0x01;
        private const uint DM_PELSWIDTH  = 0x80000;
        private const uint DM_PELSHEIGHT = 0x100000;
        private const uint DM_DISPLAYFREQUENCY = 0x400000;

        // â”€â”€ Revert timer â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static CancellationTokenSource? _revertCts = null;
        // Action appelÃ©e par le service pour notifier le client WebSocket
        public static Action<string, int, int, bool>? OnResolutionEvent;  // deviceName, w, h, isRevert

        // â”€â”€ API publique â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>Retourne tous les moniteurs actifs avec leurs modes disponibles.</summary>
        public static List<ScreenInfo> GetScreens()
        {
            var result = new List<ScreenInfo>();
            var device = new DISPLAY_DEVICE();
            device.cb = (uint)Marshal.SizeOf(device);

            for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++)
            {
                if ((device.StateFlags & DISPLAY_DEVICE_ACTIVE) == 0) continue;

                var devName = device.DeviceName;
                var current = new DEVMODE();
                current.dmSize = (short)Marshal.SizeOf(current);
                if (!EnumDisplaySettings(devName, ENUM_CURRENT_SETTINGS, ref current)) continue;

                var screen = new ScreenInfo
                {
                    Id     = devName,
                    Name   = devName.TrimStart('\\', '.', 'D', 'E', 'V', 'I', 'C', 'E').Trim('\\') // "DISPLAY1"
                                   .Replace("\\", "").Replace(".", ""),
                    Width  = (int)current.dmPelsWidth,
                    Height = (int)current.dmPelsHeight,
                    Hz     = (int)current.dmDisplayFrequency,
                };

                // Nom lisible = "DISPLAY1"
                screen.Name = device.DeviceName.Replace("\\\\.\\", "");

                // Ã‰numÃ©rer tous les modes disponibles
                var modes = new HashSet<string>();
                var mode  = new DEVMODE();
                mode.dmSize = (short)Marshal.SizeOf(mode);
                for (int m = 0; EnumDisplaySettings(devName, m, ref mode); m++)
                {
                    // Filtrer : >= 800x600, bits >= 16
                    if (mode.dmPelsWidth < 800 || mode.dmBitsPerPel < 16) continue;
                    var key = $"{mode.dmPelsWidth}x{mode.dmPelsHeight}@{mode.dmDisplayFrequency}";
                    if (modes.Add(key))
                        screen.Modes.Add(new DisplayMode
                        {
                            W  = (int)mode.dmPelsWidth,
                            H  = (int)mode.dmPelsHeight,
                            Hz = (int)mode.dmDisplayFrequency
                        });
                }

                // Trier : rÃ©solution desc, puis Hz desc
                screen.Modes.Sort((a, b) =>
                {
                    int ca = a.W * a.H, cb2 = b.W * b.H;
                    return ca != cb2 ? cb2.CompareTo(ca) : b.Hz.CompareTo(a.Hz);
                });

                result.Add(screen);
                device = new DISPLAY_DEVICE();
                device.cb = (uint)Marshal.SizeOf(device);
            }
            return result;
        }

        /// <summary>
        /// Applique une rÃ©solution. Si revertSec > 0 : temporaire avec retour auto.
        /// Retourne true si succÃ¨s.
        /// </summary>
        public static bool SetResolution(string deviceName, int w, int h, int hz,
                                         bool permanent, int revertSec = 15)
        {
            // Sauvegarder la rÃ©solution actuelle avant changement
            var current = new DEVMODE();
            current.dmSize = (short)Marshal.SizeOf(current);
            EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref current);
            int origW = (int)current.dmPelsWidth;
            int origH = (int)current.dmPelsHeight;
            int origHz = (int)current.dmDisplayFrequency;

            var dm = new DEVMODE();
            dm.dmSize             = (short)Marshal.SizeOf(dm);
            dm.dmPelsWidth        = (uint)w;
            dm.dmPelsHeight       = (uint)h;
            dm.dmDisplayFrequency = (uint)hz;
            dm.dmFields           = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

            uint flags = permanent ? CDS_UPDATEREGISTRY : 0;
            int  ret   = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, flags, IntPtr.Zero);

            if (ret != DISP_CHANGE_SUCCESSFUL) return false;

            OnResolutionEvent?.Invoke(deviceName, w, h, false);

            // Revert automatique si temporaire
            if (!permanent && revertSec > 0)
            {
                _revertCts?.Cancel();
                _revertCts = new CancellationTokenSource();
                var token = _revertCts.Token;
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(revertSec * 1000, token);
                        if (!token.IsCancellationRequested)
                            RevertResolution(deviceName, origW, origH, origHz);
                    }
                    catch (TaskCanceledException) { /* annulÃ© par KeepResolution */ }
                }, token);
            }

            return true;
        }

        /// <summary>Annule le revert automatique (l'utilisateur a confirmÃ© "Conserver").</summary>
        public static void KeepResolution()
        {
            _revertCts?.Cancel();
            _revertCts = null;
        }

        /// <summary>Force le retour immÃ©diat Ã  la rÃ©solution d'origine.</summary>
        public static void RevertResolution(string deviceName, int w, int h, int hz)
        {
            var dm = new DEVMODE();
            dm.dmSize             = (short)Marshal.SizeOf(dm);
            dm.dmPelsWidth        = (uint)w;
            dm.dmPelsHeight       = (uint)h;
            dm.dmDisplayFrequency = (uint)hz;
            dm.dmFields           = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
            ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            OnResolutionEvent?.Invoke(deviceName, w, h, true);
        }
    }
}



