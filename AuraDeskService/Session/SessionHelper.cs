using System.Runtime.InteropServices;
using System.Diagnostics;

namespace AuraDeskService.Session;

/// <summary>
/// Lance un processus dans la session interactive de l'utilisateur connecté.
/// Nécessaire pour que SendInput fonctionne hors session 0 (service Windows).
/// </summary>
public static class SessionHelper
{
    [DllImport("kernel32.dll")]
    static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("Wtsapi32.dll")]
    static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("userenv.dll")]
    static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll")]
    static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NO_WINDOW           = 0x08000000;
    private const int  STARTF_USESHOWWINDOW       = 0x00000001;

    /// <summary>
    /// Lance un exe dans la session interactive de l'utilisateur actif.
    /// Retourne le Process ID ou -1 en cas d'échec.
    /// </summary>
    public static int LaunchInUserSession(string exePath, string args = "")
    {
        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF) return -1;

        if (!WTSQueryUserToken(sessionId, out IntPtr userToken)) return -1;

        try
        {
            CreateEnvironmentBlock(out IntPtr envBlock, userToken, false);

            var si = new STARTUPINFO
            {
                cb         = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop  = "winsta0\\default",
                dwFlags    = STARTF_USESHOWWINDOW,
                wShowWindow = 0 // SW_HIDE
            };

            string cmdLine = string.IsNullOrEmpty(args)
                ? $"\"{exePath}\""
                : $"\"{exePath}\" {args}";

            bool ok = CreateProcessAsUser(
                userToken, null, cmdLine,
                IntPtr.Zero, IntPtr.Zero, false,
                CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW,
                envBlock, null, ref si, out var pi);

            DestroyEnvironmentBlock(envBlock);

            if (!ok) return -1;

            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            return pi.dwProcessId;
        }
        finally
        {
            CloseHandle(userToken);
        }
    }

    /// <summary>
    /// Retourne l'ID de la session interactive active.
    /// </summary>
    public static uint GetActiveSessionId() => WTSGetActiveConsoleSessionId();
}
