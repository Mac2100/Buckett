using System;
using System.ComponentModel;
using System.Threading;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Buckett.Services;
using Buckett.ViewModels;

namespace Buckett.Views;

/// Full-window overlay shown while a self-update runs, plus the "an update is
/// available" prompt that the launch check raises.
public partial class UpdateOverlay : UserControl
{
    private readonly SelfUpdater _updater = SelfUpdater.Shared;
    private readonly UpdateChecker _updates = AppState.Shared.Updates;
    private readonly TranslateTransform _bob = new();
    private CancellationTokenSource? _bobbing;
    private string? _promptedVersion;

    public UpdateOverlay()
    {
        InitializeComponent();

        Bucket.RenderTransform = _bob;

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

        if (IsVisible) StartBobbing(); else StopBobbing();
    }

    /// Animates TranslateTransform.Y rather than RenderTransform: the former has
    /// a registered animator, the latter does not. The animation is applied to
    /// the control, which is what Avalonia's transform animator expects — it
    /// reaches into the control's RenderTransform itself.
    internal void StartBobbing()
    {
        if (_bobbing != null) return;
        _bobbing = new CancellationTokenSource();

        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(0.7),
            IterationCount = IterationCount.Infinite,
            PlaybackDirection = PlaybackDirection.Alternate,
            Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(TranslateTransform.YProperty, -5.0) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(TranslateTransform.YProperty, 5.0) }
                }
            }
        };

        _ = animation.RunAsync(Bucket, _bobbing.Token);
    }

    internal void StopBobbing()
    {
        _bobbing?.Cancel();
        _bobbing?.Dispose();
        _bobbing = null;
        _bob.Y = 0;
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
