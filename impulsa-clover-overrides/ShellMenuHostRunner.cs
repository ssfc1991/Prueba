using System.Runtime.InteropServices;

namespace ImpulsaExplorer.Services;

internal static class ShellMenuHostRunner
{
    public const string RequestArgument = "--shell-menu-request";

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_SHOWNA = 8;

    public static bool TryGetRequestFile(out string requestFile)
    {
        requestFile = string.Empty;
        var args = Environment.GetCommandLineArgs();
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], RequestArgument, StringComparison.OrdinalIgnoreCase))
                continue;

            requestFile = args[i + 1];
            return !string.IsNullOrWhiteSpace(requestFile);
        }

        return false;
    }

    public static int Run(string requestFile)
    {
        IntPtr hostWindow = IntPtr.Zero;
        try
        {
            if (string.IsNullOrWhiteSpace(requestFile) || !File.Exists(requestFile))
                return 10;

            var paths = File.ReadAllLines(requestFile)
                .Where(path => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (paths.Length == 0)
                return 11;

            // A tiny off-screen top-level window gives Shell extensions an HWND
            // owned by this helper process. Any extension crash is therefore
            // isolated from the main Impulsa Explorer process.
            hostWindow = CreateWindowExW(
                WS_EX_TOOLWINDOW,
                "STATIC",
                "Impulsa Shell Menu Host",
                WS_POPUP,
                -32000,
                -32000,
                1,
                1,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (hostWindow == IntPtr.Zero)
                return 12;

            ShowWindow(hostWindow, SW_SHOWNA);
            SetForegroundWindow(hostWindow);

            return NativeShellContextMenuService.Show(paths) ? 0 : 13;
        }
        catch
        {
            return 20;
        }
        finally
        {
            try
            {
                if (hostWindow != IntPtr.Zero)
                    DestroyWindow(hostWindow);
            }
            catch
            {
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(requestFile) && File.Exists(requestFile))
                    File.Delete(requestFile);
            }
            catch
            {
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
