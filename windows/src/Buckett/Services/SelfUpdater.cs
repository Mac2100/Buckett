using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Buckett.Services;

public enum UpdatePhase { Idle, Downloading, Installing, Relaunching, Failed }

/// In-place self-update: downloads the release archive, stages it, then hands
/// off to a small script that waits for this process to exit, swaps the install
/// folder's contents, and relaunches Buckett. No installer framework needed;
/// works with the portable ZIP that CI publishes to GitHub Releases.
public sealed class SelfUpdater : ObservableObject
{
    public static SelfUpdater Shared { get; } = new();

    private static readonly HttpClient Http = new() { Timeout = Timeout };
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    private UpdatePhase _phase = UpdatePhase.Idle;
    private string? _failureMessage;
    private double _downloadProgress;

    private SelfUpdater() { }

    public UpdatePhase Phase
    {
        get => _phase;
        private set
        {
            if (!SetProperty(ref _phase, value)) return;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsDownloading));
            OnPropertyChanged(nameof(IsInstalling));
            OnPropertyChanged(nameof(IsRelaunching));
            OnPropertyChanged(nameof(HasFailed));
        }
    }

    public string? FailureMessage
    {
        get => _failureMessage;
        private set => SetProperty(ref _failureMessage, value);
    }

    /// 0…1 while `Phase == Downloading`.
    public double DownloadProgress
    {
        get => _downloadProgress;
        private set
        {
            if (SetProperty(ref _downloadProgress, value)) OnPropertyChanged(nameof(ProgressText));
        }
    }

    public string ProgressText => $"Downloading update… {(int)(DownloadProgress * 100)}%";

    public bool IsBusy => Phase is UpdatePhase.Downloading or UpdatePhase.Installing or UpdatePhase.Relaunching;
    public bool IsDownloading => Phase == UpdatePhase.Downloading;
    public bool IsInstalling => Phase == UpdatePhase.Installing;
    public bool IsRelaunching => Phase == UpdatePhase.Relaunching;
    public bool HasFailed => Phase == UpdatePhase.Failed;

    /// Kicks off download + install + relaunch. Falls back to opening the URL
    /// in the browser when the release has no portable archive.
    public void Install(string url)
    {
        if (IsBusy) return;
        if (!url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ShellHelper.OpenUrl(url);
            return;
        }

        Phase = UpdatePhase.Downloading;
        FailureMessage = null;
        DownloadProgress = 0;

        _ = Task.Run(async () =>
        {
            try
            {
                var archive = await DownloadWithProgressAsync(url).ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() => Phase = UpdatePhase.Installing);
                var target = InstallDirectory();
                var staged = Stage(archive, target);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Phase = UpdatePhase.Relaunching;
                    Relaunch(staged, target);
                });
            }
            catch (Exception error)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Phase = UpdatePhase.Failed;
                    FailureMessage = error.Message;
                    ToastCenter.Shared.Show("Update failed", error.Message, ToastStyle.Error);
                });
            }
        });
    }

    private static string InstallDirectory()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not locate the running executable.");
        return Path.GetDirectoryName(executable)
            ?? throw new InvalidOperationException("Could not locate the install folder.");
    }

    private async Task<string> DownloadWithProgressAsync(string url)
    {
        var destination = Path.Combine(
            Path.GetTempPath(), $"Buckett-update-{Guid.NewGuid():N}.zip");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "Buckett");
        using var response = await Http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Download failed (HTTP {(int)response.StatusCode}).");
        }

        var total = response.Content.Headers.ContentLength ?? 0;
        await using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
        await using (var file = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
        {
            var buffer = new byte[128 * 1024];
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                written += read;
                if (total > 0)
                {
                    var fraction = (double)written / total;
                    Dispatcher.UIThread.Post(() => DownloadProgress = fraction);
                }
            }
        }
        return destination;
    }

    /// Extracts the archive and returns the folder that holds the new Buckett.exe.
    private static string Stage(string archivePath, string target)
    {
        var staged = Path.Combine(Path.GetTempPath(), $"Buckett-staged-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staged);
        ZipFile.ExtractToDirectory(archivePath, staged, overwriteFiles: true);
        try { File.Delete(archivePath); } catch { /* best effort */ }

        var root = FindExecutableRoot(staged)
            ?? throw new InvalidOperationException("No Buckett.exe found inside the update archive.");

        if (!IsWritable(target))
        {
            throw new InvalidOperationException(
                $"Cannot write to {target}. Move Buckett to a writable location " +
                "(for example a folder under your user profile) and try again.");
        }
        return root;
    }

    private static string? FindExecutableRoot(string directory)
    {
        if (File.Exists(Path.Combine(directory, "Buckett.exe"))) return directory;
        foreach (var child in Directory.GetDirectories(directory))
        {
            var found = FindExecutableRoot(child);
            if (found != null) return found;
        }
        return null;
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".buckett-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// Writes and launches the swap script, then quits so the files are unlocked.
    private static void Relaunch(string staged, string target)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"buckett-update-{Guid.NewGuid():N}.cmd");
        var pid = Environment.ProcessId;
        var executable = Path.Combine(target, "Buckett.exe");

        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine("setlocal");
        // Wait for Buckett to exit so its files are no longer locked.
        script.AppendLine(":waitloop");
        script.AppendLine($"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul");
        script.AppendLine("if not errorlevel 1 (");
        script.AppendLine("  ping -n 2 127.0.0.1 >nul");
        script.AppendLine("  goto waitloop");
        script.AppendLine(")");
        // /E copies subfolders without purging anything the user keeps alongside.
        script.AppendLine($"robocopy \"{staged}\" \"{target}\" /E /R:4 /W:1 /NFL /NDL /NJH /NJS /NP >nul");
        script.AppendLine($"start \"\" \"{executable}\"");
        script.AppendLine($"rmdir /s /q \"{staged}\"");
        script.AppendLine("del \"%~f0\"");

        File.WriteAllText(scriptPath, script.ToString(), Encoding.ASCII);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });

        // Give the helper a moment to start watching before we go away.
        DispatcherTimer.RunOnce(
            () =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime
                    is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
                else
                {
                    Environment.Exit(0);
                }
            },
            TimeSpan.FromMilliseconds(600));
    }
}
