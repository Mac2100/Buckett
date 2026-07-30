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
    /// notification area; the window comes back from the tray menu or the
    /// desktop drop target. With both of those turned off there would be no way
    /// back, so then the close really does quit.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var hasWayBack = Settings.Shared.ShowTrayIcon || Settings.Shared.ShowDropTarget;
        if (!App.IsShuttingDown && hasWayBack)
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
        Activate();
        Topmost = true;
        Topmost = false;
    }
}
