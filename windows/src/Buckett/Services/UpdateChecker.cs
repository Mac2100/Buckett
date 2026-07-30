using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Buckett.Support;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Buckett.Services;

public enum UpdateStatusKind
{
    Idle,
    Checking,
    UpToDate,
    /// GitHub answered 404: either no release exists yet, or the repository
    /// is private and anonymous API calls cannot see its releases.
    NoReleasesVisible,
    UpdateAvailable,
    Failed
}

public readonly record struct UpdateStatus(
    UpdateStatusKind Kind,
    string? Version = null,
    string? Url = null,
    string? Message = null)
{
    public static readonly UpdateStatus Idle = new(UpdateStatusKind.Idle);
    public static readonly UpdateStatus Checking = new(UpdateStatusKind.Checking);
    public static readonly UpdateStatus UpToDate = new(UpdateStatusKind.UpToDate);
    public static readonly UpdateStatus NoReleasesVisible = new(UpdateStatusKind.NoReleasesVisible);

    public static UpdateStatus Available(string version, string url) =>
        new(UpdateStatusKind.UpdateAvailable, version, url);

    public static UpdateStatus Failed(string message) =>
        new(UpdateStatusKind.Failed, Message: message);
}

/// Checks GitHub Releases for a newer version of Buckett.
public sealed class UpdateChecker : ObservableObject
{
    public static string Repo => SupportLinks.Repo;
    public static string ReleasesPage => SupportLinks.ReleasesPage;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private UpdateStatus _status = UpdateStatus.Idle;
    private DateTime? _lastChecked;

    public UpdateStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsChecking));
                OnPropertyChanged(nameof(IsUpdateAvailable));
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(HasStatusMessage));
                OnPropertyChanged(nameof(StatusSymbol));
                OnPropertyChanged(nameof(StatusBrushKey));
            }
        }
    }

    public DateTime? LastChecked
    {
        get => _lastChecked;
        private set
        {
            if (SetProperty(ref _lastChecked, value)) OnPropertyChanged(nameof(LastCheckedText));
        }
    }

    public bool IsChecking => Status.Kind == UpdateStatusKind.Checking;
    public bool IsUpdateAvailable => Status.Kind == UpdateStatusKind.UpdateAvailable;

    public string LastCheckedText =>
        LastChecked is { } date ? $"Last checked {date:t}" : "";

    public bool HasStatusMessage => StatusMessage.Length > 0;

    public string StatusMessage => Status.Kind switch
    {
        UpdateStatusKind.UpToDate => "You're up to date",
        UpdateStatusKind.NoReleasesVisible =>
            "No releases visible — private repositories can't be checked anonymously",
        UpdateStatusKind.UpdateAvailable => $"Version {Status.Version} is available",
        UpdateStatusKind.Failed => Status.Message ?? "Update check failed",
        _ => ""
    };

    public string StatusSymbol => Status.Kind switch
    {
        UpdateStatusKind.UpToDate => "checkmark.circle.fill",
        UpdateStatusKind.NoReleasesVisible => "eye.slash",
        UpdateStatusKind.UpdateAvailable => "arrow.down.circle.fill",
        _ => "exclamationmark.triangle.fill"
    };

    public string StatusBrushKey => Status.Kind switch
    {
        UpdateStatusKind.UpToDate => "SuccessBrush",
        UpdateStatusKind.UpdateAvailable => "InfoBrush",
        _ => "WarningBrush"
    };

    private sealed class Release
    {
        public string? tag_name { get; set; }
        public string? html_url { get; set; }
        public Asset[]? assets { get; set; }

        public sealed class Asset
        {
            public string? name { get; set; }
            public string? browser_download_url { get; set; }
        }
    }

    public void CheckOnLaunchIfEnabled()
    {
        if (!Settings.Shared.AutoCheckUpdates) return;
        _ = CheckAsync();
    }

    /// Runs a check and, for user-initiated checks, surfaces the "nothing new"
    /// outcomes as toasts (silent launch checks stay silent).
    public async Task CheckAsync(bool userInitiated)
    {
        await CheckAsync().ConfigureAwait(true);
        if (!userInitiated) return;

        switch (Status.Kind)
        {
            case UpdateStatusKind.UpToDate:
                ToastCenter.Shared.Show(
                    "No updates available",
                    $"Buckett {AppVersion.Current} is the latest version");
                break;
            case UpdateStatusKind.NoReleasesVisible:
                ToastCenter.Shared.Show(
                    "No releases visible",
                    "Private repositories can't be checked anonymously",
                    ToastStyle.Info);
                break;
            case UpdateStatusKind.Failed:
                ToastCenter.Shared.Show(
                    "Update check failed", Status.Message, ToastStyle.Error);
                break;
        }
    }

    public async Task CheckAsync()
    {
        Status = UpdateStatus.Checking;
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("User-Agent", $"Buckett/{AppVersion.Current}");

            using var response = await Http.SendAsync(request).ConfigureAwait(true);
            if ((int)response.StatusCode == 404)
            {
                Status = UpdateStatus.NoReleasesVisible;
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                Status = UpdateStatus.Failed($"GitHub responded with HTTP {(int)response.StatusCode}.");
                return;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
            var release = JsonSerializer.Deserialize<Release>(json);
            var tag = release?.tag_name ?? "";
            var latest = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;

            if (latest.Length > 0 && IsVersionNewer(latest, AppVersion.Current))
            {
                Status = UpdateStatus.Available(latest, PickAsset(release) ?? ReleasesPage);
            }
            else
            {
                Status = UpdateStatus.UpToDate;
            }
        }
        catch (Exception error)
        {
            Status = UpdateStatus.Failed(error.Message);
        }
        finally
        {
            LastChecked = DateTime.Now;
        }
    }

    /// Prefers the Windows archive built by CI, then any installer, then the
    /// release page (which the updater simply opens in a browser).
    private static string? PickAsset(Release? release)
    {
        var assets = release?.assets;
        if (assets is { Length: > 0 })
        {
            bool Windowsish(Release.Asset asset) =>
                (asset.name ?? "").Contains("win", StringComparison.OrdinalIgnoreCase);

            var zip = assets.FirstOrDefault(asset =>
                          (asset.name ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                          && Windowsish(asset))
                      ?? assets.FirstOrDefault(asset =>
                          (asset.name ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (zip?.browser_download_url is { Length: > 0 } zipUrl) return zipUrl;

            var installer = assets.FirstOrDefault(asset =>
                (asset.name ?? "").EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                (asset.name ?? "").EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
            if (installer?.browser_download_url is { Length: > 0 } installerUrl) return installerUrl;
        }
        return release?.html_url;
    }

    /// Numeric dotted-version comparison ("1.2.10" > "1.2.9").
    public static bool IsVersionNewer(string a, string b)
    {
        static int[] Parts(string value) => value
            .Split('.')
            .Select(part =>
            {
                var digits = new string(part.TakeWhile(char.IsDigit).ToArray());
                return int.TryParse(digits, out var parsed) ? parsed : 0;
            })
            .ToArray();

        var left = Parts(a);
        var right = Parts(b);
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var x = i < left.Length ? left[i] : 0;
            var y = i < right.Length ? right[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }
}
