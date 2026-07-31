using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Buckett.Services;
using Buckett.ViewModels;
using Buckett.Views;

namespace Buckett.Shell;

/// The notification-area presence: an icon whose menu opens the app, shows
/// active transfers and quits, and which carries Buckett's system
/// notifications. It does not accept dropped files — Windows notification-area
/// icons cannot — so uploads by drag and drop go to the main window.
public sealed class TrayController : IDisposable
{
    public static TrayController Shared { get; } = new();

    private NativeTray? _tray;
    private string _iconStyle = "";
    private bool _iconIsLight;

    private TrayController() { }

    public void Setup()
    {
        _tray = new NativeTray();
        if (!_tray.IsAvailable)
        {
            Log.Warn("notification-area icon unavailable; running without it");
            _tray = null;
        }
        else
        {
            _tray.MenuProvider = BuildMenu;
            _tray.Activated += () => AppState.Shared.OpenMainWindow();
            Notifier.Shared.Tray = _tray;
            RefreshIcon();
            _tray.Show("Buckett");
            _tray.SetHidden(!Settings.Shared.ShowTrayIcon);
        }

        Settings.Shared.PropertyChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(Settings.ShowTrayIcon):
                    _tray?.SetHidden(!Settings.Shared.ShowTrayIcon);
                    break;
                case nameof(Settings.TrayIconStyle):
                    RefreshIcon();
                    break;
            }
        });
    }

    /// Re-renders the notification-area icon for the current glyph style and
    /// taskbar brightness.
    public void RefreshIcon()
    {
        if (_tray == null) return;
        var style = Settings.Shared.TrayIconStyle;
        var light = TrayIconRenderer.TaskbarIsLight();
        if (style == _iconStyle && light == _iconIsLight) return;

        var icon = TrayIconRenderer.CreateIcon(style);
        if (icon == IntPtr.Zero) return;

        _tray.SetIcon(icon);
        _iconStyle = style;
        _iconIsLight = light;
    }

    // MARK: - Menu

    private List<TrayMenuItem> BuildMenu()
    {
        var state = AppState.Shared;
        var items = new List<TrayMenuItem>
        {
            new()
            {
                Title = "Open Buckett",
                IsDefault = true,
                Action = () => Dispatcher.UIThread.Post(state.OpenMainWindow)
            },
            TrayMenuItem.Separator()
        };

        var active = state.Transfers.ActiveCount;
        if (active > 0)
        {
            items.Add(new TrayMenuItem
            {
                Title = $"{active} transfer{(active == 1 ? "" : "s")} active",
                IsEnabled = false
            });
        }

        items.Add(TrayMenuItem.Separator());
        items.Add(new TrayMenuItem
        {
            Title = "Settings…",
            Action = () => Dispatcher.UIThread.Post(() => SettingsWindow.Present(null))
        });
        items.Add(new TrayMenuItem
        {
            Title = "Quit Buckett",
            Action = () => Dispatcher.UIThread.Post(Quit)
        });

        return items;
    }

    private static void Quit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            App.IsShuttingDown = true;
            desktop.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }

    public void Dispose()
    {
        Settings.Shared.PropertyChanged -= OnSettingsChanged;
        _tray?.Dispose();
        _tray = null;
    }
}
