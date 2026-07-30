using System;
using System.Diagnostics;
using System.Reflection;

namespace Buckett.Support;

/// Single source of truth for the app version.
/// `build/make_app.ps1` extracts this value to stamp the assembly and name the ZIP.
public static class AppVersion
{
    public const string Marketing = "1.7.4";

    /// Prefers the version baked into the executable, falls back to the
    /// compiled-in constant when that is unavailable.
    public static string Current
    {
        get
        {
            try
            {
                var informational = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(informational))
                {
                    // Strip any "+<commit sha>" build metadata.
                    var plus = informational!.IndexOf('+');
                    var value = plus > 0 ? informational[..plus] : informational;
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
                var fileVersion = FileVersionInfo
                    .GetVersionInfo(Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location)
                    .ProductVersion;
                if (!string.IsNullOrWhiteSpace(fileVersion)) return fileVersion!;
            }
            catch
            {
                // Fall through to the constant.
            }
            return Marketing;
        }
    }
}

/// The one place the "support this app" destinations live.
public static class SupportLinks
{
    public const string Repo = "Mac2100/Buckett";
    public static string BuyMeACoffee => "https://www.buymeacoffee.com/Mac2100";
    public static string GitHubRepo => $"https://github.com/{Repo}";
    public static string ReleasesPage => $"https://github.com/{Repo}/releases/latest";
    public static string License => $"https://github.com/{Repo}/blob/main/LICENSE";
}
