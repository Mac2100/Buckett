using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Buckett.Services;

/// Thin wrappers around the Windows shell — the NSWorkspace equivalents.
public static class ShellHelper
{
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            Log.Warn($"could not open {url}: {error.Message}");
        }
    }

    /// Opens a local file with whatever application is registered for it.
    public static void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            Log.Warn($"could not open {path}: {error.Message}");
            ToastCenter.Shared.Show("Couldn't open file", error.Message, ToastStyle.Error);
        }
    }

    /// Opens Explorer with the file selected — the "Show in Finder" counterpart.
    public static void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true
                });
            }
            else
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"")
                    {
                        UseShellExecute = true
                    });
                }
            }
        }
        catch (Exception error)
        {
            Log.Warn($"could not reveal {path}: {error.Message}");
        }
    }
}

/// "Open at login", implemented with the per-user Run key — the Windows
/// counterpart of SMAppService.mainApp.register().
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Buckett";

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) != null;
        }
        catch (Exception error)
        {
            Log.Warn($"could not read startup registration: {error.Message}");
            return false;
        }
    }

    /// Throws on failure so the settings toggle can surface the reason.
    public static void SetRegistered(bool registered)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the Windows startup registry key.");

        if (registered)
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                throw new InvalidOperationException("Could not determine the Buckett executable path.");
            }
            key.SetValue(ValueName, $"\"{executable}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
