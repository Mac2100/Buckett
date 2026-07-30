using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Buckett.Tests;
using Buckett.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Buckett.Tests;

public static class TestAppBuilder
{
    /// The real App, so these tests load the real styles and resources.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Buckett.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// Building the main window is where startup actually failed once: the window
/// hosts the update overlay, which touches SelfUpdater, whose static
/// initialiser threw. Every unit test passed and CI was green, because nothing
/// had ever constructed the window. So construct it here.
public class MainWindowTests
{
    [AvaloniaFact]
    public void MainWindowConstructs()
    {
        var window = new MainWindow();
        Assert.NotNull(window);
    }

    [AvaloniaFact]
    public void MainWindowShowsTheWelcomeScreenWithNoAccounts()
    {
        // A fresh install has no accounts, so this is the very first thing a
        // new user sees.
        var window = new MainWindow();
        window.Show();
        Assert.NotNull(window.Content);
    }

    /// The overlays live in the main window and reach for the singletons that
    /// broke, so they get built explicitly too.
    [AvaloniaFact]
    public void OverlaysConstruct()
    {
        Assert.NotNull(new ToastHost());
        Assert.NotNull(new UpdateOverlay());
    }

    [AvaloniaFact]
    public void SidebarAndDetailViewsConstruct()
    {
        Assert.NotNull(new SidebarView());
        Assert.NotNull(new WelcomeView());
        Assert.NotNull(new MissingCredentialsView());
        Assert.NotNull(new DashboardView());
    }

    /// The floating drop target is created during startup whenever the setting
    /// is on, which it is by default.
    [AvaloniaFact]
    public void DropTargetConstructs()
    {
        Assert.NotNull(new DropPadWindow());
    }

    /// The theme palette builds Avalonia brushes, so it is only ever valid on
    /// the UI thread — which is where the app touches it. Pinned here rather
    /// than alongside the background-safe initialisers.
    [AvaloniaFact]
    public void ThemePaletteResolves()
    {
        Assert.NotEmpty(Themes.All);
        foreach (var theme in Themes.All)
        {
            Assert.False(string.IsNullOrEmpty(theme.Id));
            Assert.NotNull(theme.PrimaryBrush);
            Assert.NotNull(theme.Gradient);
        }

        var selected = ThemeStore.Shared.Theme;
        Assert.NotNull(selected);
        Assert.Contains(Themes.All, candidate => candidate.Id == selected.Id);
    }

    /// Applying the theme writes into the application's resources, which is
    /// the first thing the app does on startup.
    [AvaloniaFact]
    public void ApplyingTheThemeSucceeds()
    {
        ThemeStore.Shared.ApplyAll();
        Assert.NotNull(Application.Current);
        Assert.True(Application.Current!.Resources.ContainsKey("ThemePrimaryBrush"));
    }
}
