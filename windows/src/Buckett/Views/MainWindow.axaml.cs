using System;
using System.ComponentModel;
using Avalonia.Controls;
using Buckett.Services;
using Buckett.ViewModels;

namespace Buckett.Views;

public partial class MainWindow : Window
{
    private readonly AppState _state = AppState.Shared;
    private BucketDetailView? _detail;

    public MainWindow()
    {
        InitializeComponent();

        // A 1280x820 window does not fit an 800-tall laptop panel, and Avalonia
        // would centre it anyway — title bar above the top of the screen.
        this.KeepOnScreen();

        ClipboardService.Register(this);
        Panels.Register(this);

        _state.PropertyChanged += OnStatePropertyChanged;
        _state.BucketsChanged += UpdateDetail;
        _state.AccountStore.Accounts.CollectionChanged += (_, _) => UpdateDetail();

        UpdateDetail();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _state.Updates.CheckOnLaunchIfEnabled();
        _ = _state.LoadBucketsAsync();
    }

    /// Closing the window must NOT quit the app — Buckett lives on in the
    /// notification area, and the window comes back from the tray menu. With
    /// the tray icon turned off there would be no way back, so then the close
    /// really does quit.
    ///
    /// Only the user clicking the close button gets that treatment. Windows
    /// logging off, or an installer's restart manager asking the app to go
    /// away, must be obeyed: refusing left Buckett running after it had
    /// supposedly been uninstalled.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var hasWayBack = Settings.Shared.ShowTrayIcon;
        if (ShouldHideToTray(e.CloseReason, App.IsShuttingDown, hasWayBack))
        {
            e.Cancel = true;
            Hide();
            return;
        }

        App.IsShuttingDown = true;
        base.OnClosing(e);
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// The one case that hides instead of quitting: the user clicked the close
    /// button and there is still a way to get the window back. Everything else
    /// — the tray's Quit item, the updater restarting the app, Windows logging
    /// off, an installer's restart manager clearing the way for an uninstall —
    /// is a real request to go away, and cancelling it left Buckett resident
    /// with no window and, after an uninstall, no executable on disk.
    internal static bool ShouldHideToTray(
        WindowCloseReason reason, bool shuttingDown, bool hasWayBack) =>
        !shuttingDown && hasWayBack && reason == WindowCloseReason.WindowClosing;

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.SidebarSelection)
            or nameof(AppState.SelectedAccountID)
            or nameof(AppState.HasAccounts))
        {
            UpdateDetail();
        }
    }

    /// Mirrors ContentView's `detail` switch: welcome → bucket → dashboard.
    private void UpdateDetail()
    {
        if (!_state.HasAccounts)
        {
            if (DetailHost.Content is not WelcomeView) DetailHost.Content = new WelcomeView();
            _detail = null;
            return;
        }

        var bucket = _state.SidebarSelection.Bucket;
        if (bucket == null)
        {
            if (DetailHost.Content is not DashboardView) DetailHost.Content = new DashboardView();
            _detail = null;
            return;
        }

        var client = _state.CurrentClient;
        if (client == null)
        {
            if (DetailHost.Content is not MissingCredentialsView)
            {
                DetailHost.Content = new MissingCredentialsView();
            }
            _detail = null;
            return;
        }

        var identity = $"{_state.SelectedAccountID}/{bucket}";
        if (_detail?.Identity == identity) return;

        _detail = new BucketDetailView(bucket, client, identity);
        DetailHost.Content = _detail;
    }

    /// Brings the window back after it was closed to the tray.
    public void Reveal()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        // The display layout may have changed while the window sat in the tray.
        this.EnsureOnScreen();
        Activate();
        Topmost = true;
        Topmost = false;
    }
}
