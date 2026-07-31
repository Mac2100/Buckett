using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Buckett.Services;

namespace Buckett;

internal static class Program
{
    private static int _reported;

    [STAThread]
    public static void Main(string[] args)
    {
        // Buckett is a WinExe, so it has no console to print to. Without these
        // handlers an exception during startup ends the process silently and the
        // user simply sees nothing happen when they launch the app.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportFatal(e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Warn($"unobserved background task error: {e.Exception.Message}");
            e.SetObserved();
        };

        // A second launch just brings the running copy forward. Doing this
        // before Avalonia starts keeps the duplicate from ever drawing a tray
        // icon.
        if (!SingleInstance.TryAcquire()) return;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error)
        {
            ReportFatal(error);
            Environment.ExitCode = 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// Writes the failure somewhere it can be read afterwards, and tells the
    /// user rather than vanishing. Must never throw on its own.
    private static void ReportFatal(Exception? error)
    {
        // A crash can surface through both the try/catch and the unhandled
        // handler; only the first one reports.
        if (Interlocked.Exchange(ref _reported, 1) != 0) return;

        var details = error?.ToString() ?? "Unknown error.";
        var logPath = "(could not write a log file)";
        try
        {
            AppPaths.EnsureSupportDirectory();
            logPath = Path.Combine(AppPaths.SupportDirectory, "crash.log");
            File.AppendAllText(
                logPath,
                $"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---{Environment.NewLine}" +
                $"{details}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Reporting the crash must not cause another one.
        }

        try
        {
            var summary = error?.Message ?? "Unknown error.";
            MessageBox(
                IntPtr.Zero,
                $"Buckett couldn't start.\n\n{summary}\n\nDetails were written to:\n{logPath}",
                "Buckett",
                MB_OK | MB_ICONERROR);
        }
        catch
        {
            // No UI available; the log above is the record.
        }
    }

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
