using System.ComponentModel;
using Avalonia.Controls;
using Buckett.Services;
using Buckett.ViewModels;

namespace Buckett.Views;

/// Full-window overlay shown while a self-update runs, plus the "an update is
/// available" prompt that the launch check raises.
public partial class UpdateOverlay : UserControl
{
    private readonly SelfUpdater _updater = SelfUpdater.Shared;
    private readonly UpdateChecker _updates = AppState.Shared.Updates;
    private string? _promptedVersion;

    public UpdateOverlay()
    {
        InitializeComponent();

        _updater.PropertyChanged += OnUpdaterChanged;
        _updates.PropertyChanged += OnUpdatesChanged;
        Sync();
    }

    private void OnUpdaterChanged(object? sender, PropertyChangedEventArgs e) => Sync();

    private void Sync()
    {
        IsVisible = _updater.IsBusy;
        DownloadingBlock.IsVisible = _updater.IsDownloading;
        InstallingBlock.IsVisible = _updater.IsInstalling;
        RelaunchingLabel.IsVisible = _updater.IsRelaunching;
        DownloadProgress.Value = _updater.DownloadProgress;
        DownloadLabel.Text = _updater.ProgressText;
    }

    private void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UpdateChecker.Status)) return;
        var status = _updates.Status;
        if (status.Kind != UpdateStatusKind.UpdateAvailable) return;
        if (status.Version == null || status.Url == null) return;
        if (_promptedVersion == status.Version) return;

        _promptedVersion = status.Version;
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            UpdateAvailableWindow.Show(owner, status.Version, status.Url);
        }
    }
}
