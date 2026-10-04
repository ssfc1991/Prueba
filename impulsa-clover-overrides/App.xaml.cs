using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace ImpulsaExplorer;

public partial class App : Application
{
    public static Window? MainAppWindow { get; private set; }

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "ImpulsaExplorer_Crash.log");

    public App()
    {
        TryDeleteOldCrashLog();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteLog("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteLog("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        UnhandledException += (_, e) =>
        {
            WriteLog("Application.UnhandledException", e.Exception);
            ShowStartupError(e.Exception);
            e.Handled = true;
        };

        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            WriteLog("Fallo en App.InitializeComponent", ex);
            ShowStartupError(ex);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            MainAppWindow = new MainWindow();
            MainAppWindow.Activate();
        }
        catch (Exception ex)
        {
            WriteLog("Fallo al crear/activar MainWindow", ex);
            ShowStartupError(ex);
        }
    }

    public static void WriteLog(string message, Exception? ex = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            using var writer = new StreamWriter(LogPath, append: true);
            writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
            if (ex is not null)
            {
                writer.WriteLine(ex.ToString());
                writer.WriteLine(new string('-', 80));
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteOldCrashLog()
    {
        try
        {
            if (File.Exists(LogPath))
                File.Delete(LogPath);
        }
        catch
        {
        }
    }

    private static void ShowStartupError(Exception ex)
    {
        try
        {
            MessageBox(IntPtr.Zero,
                $"Impulsa Explorer no pudo iniciar.\n\n{ex.GetType().Name}: {ex.Message}\n\nSe creó ImpulsaExplorer_Crash.log en el Escritorio.",
                "Impulsa Explorer - Error de inicio",
                0x00000010);
        }
        catch
        {
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
