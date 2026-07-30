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

/// The notification-area presence: an icon whose menu mirrors the macOS menu
/// bar item (open the app, pick which buckets the drop target offers, see
/// active transfers, quit) and which carries Buckett's system notifications.
/// Dropping files is handled by the companion `DropPadWindow`, because Windows
/// notification-area icons cannot accept dropped files.
public sealed class TrayController : IDisposable
{
    public static TrayController Shared { get; } = new();

    private NativeTray? _tray;
    private DropPadWindow? _dropPad;
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
            _tray.Show("Buckett — drag files onto the desktop drop target to upload");
            _tray.SetHidden(!Settings.Shared.ShowTrayIcon);
        }

        Settings.Shared.PropertyChanged += OnSettingsChanged;
        SetDropTargetVisible(Settings.Shared.ShowDropTarget);
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
                case nameof(Settings.ShowDropTarget):
                    SetDropTargetVisible(Settings.Shared.ShowDropTarget);
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

    // MARK: - Desktop drop target

    public void SetDropTargetVisible(bool visible)
    {
        try
        {
            if (visible)
            {
                if (_dropPad != null) return;
                _dropPad = new DropPadWindow();
                _dropPad.Closed += (_, _) => _dropPad = null;
                _dropPad.Show();
            }
            else
            {
                _dropPad?.Close();
                _dropPad = null;
                DropOverlays.Shared.CloseHover();
            }
        }
        catch (Exception error)
        {
            // A transparent, always-on-top window is the most likely thing to
            // be refused by an unusual display setup or a remote session. That
            // must not take the rest of the app with it.
            Log.Warn($"drop target unavailable: {error.Message}");
            _dropPad = null;
        }
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

        var shortlist = Settings.Shared.TrayDropBuckets.ToHashSet();
        var submenu = new List<TrayMenuItem>
        {
            new() { Title = "Check buckets to offer when dropping files", IsEnabled = false }
        };

        var addedAny = false;
        foreach (var account in state.AccountStore.Accounts)
        {
            var buckets = state.BucketList(account.Id);
            if (buckets.Count == 0) continue;

            submenu.Add(TrayMenuItem.Separator());
            submenu.Add(new TrayMenuItem
            {
                Title = account.DisplayLabel.ToUpperInvariant(),
                IsEnabled = false
            });

            foreach (var bucket in buckets)
            {
                var target = new DropTarget(account.Id, bucket.Name);
                var encoded = target.Encoded;
                submenu.Add(new TrayMenuItem
                {
                    Title = BucketAliases.Shared.DisplayName(account.Id, bucket.Name),
                    IsChecked = shortlist.Contains(encoded),
                    Action = () => Dispatcher.UIThread.Post(() => ToggleShortlist(encoded))
                });
                addedAny = true;
            }
        }

        if (!addedAny)
        {
            submenu.Add(new TrayMenuItem { Title = "No buckets loaded yet", IsEnabled = false });
        }

        items.Add(new TrayMenuItem { Title = "Drop Menu Buckets", Submenu = submenu });

        items.Add(new TrayMenuItem
        {
            Title = Settings.Shared.ShowDropTarget ? "Hide Drop Target" : "Show Drop Target",
            Action = () => Dispatcher.UIThread.Post(
                () => Settings.Shared.ShowDropTarget = !Settings.Shared.ShowDropTarget)
        });

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

    private static void ToggleShortlist(string encoded)
    {
        var shortlist = Settings.Shared.TrayDropBuckets.ToList();
        if (!shortlist.Remove(encoded)) shortlist.Add(encoded);
        Settings.Shared.SetTrayDropBuckets(shortlist);
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
        _dropPad?.Close();
        _dropPad = null;
        _tray?.Dispose();
        _tray = null;
    }
}
