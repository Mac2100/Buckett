using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Buckett.Services;
using Buckett.Shell;
using Buckett.ViewModels;
using Buckett.Views;

namespace Buckett;

public partial class App : Application
{
    /// Set when the user really means to quit, so closing the main window can
    /// keep the app alive in the notification area the rest of the time.
    public static bool IsShuttingDown { get; set; }

    private MainWindow? _mainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ThemeStore.Shared.ApplyAll();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Buckett lives on after its window is closed, like the macOS build.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;

            AppState.Shared.OpenMainWindowRequested += RevealMainWindow;

            desktop.Exit += (_, _) =>
            {
                IsShuttingDown = true;
                TrayController.Shared.Dispose();
                Settings.Shared.Save();
            };

            _mainWindow.Show();
            TrayController.Shared.Setup();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void RevealMainWindow()
    {
        if (_mainWindow == null) return;
        if (Dispatcher.UIThread.CheckAccess())
        {
            _mainWindow.Reveal();
        }
        else
        {
            Dispatcher.UIThread.Post(() => _mainWindow.Reveal());
        }
    }
}
