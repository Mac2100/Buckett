using Avalonia.Controls;
using Buckett.Views;
using Xunit;

namespace Buckett.Tests;

/// The main window hides to the notification area instead of closing, which is
/// right when the user clicks the close button and wrong for every other kind
/// of close request. Cancelling those left Buckett running after its own
/// uninstaller had removed it — tray icon and desktop drop target still on
/// screen, executable already gone from disk.
public class ShutdownTests
{
    [Fact]
    public void ClickingCloseHidesToTheTray()
    {
        Assert.True(MainWindow.ShouldHideToTray(
            WindowCloseReason.WindowClosing, shuttingDown: false, hasWayBack: true));
    }

    /// With no tray icon and no drop target there is nothing to come back from,
    /// so the close has to be a real quit.
    [Fact]
    public void ClosingQuitsWhenThereIsNoWayBack()
    {
        Assert.False(MainWindow.ShouldHideToTray(
            WindowCloseReason.WindowClosing, shuttingDown: false, hasWayBack: false));
    }

    /// Windows logging off or shutting down. Refusing this is what stranded the
    /// process; it also blocks a clean reboot.
    [Fact]
    public void OsShutdownIsObeyed()
    {
        Assert.False(MainWindow.ShouldHideToTray(
            WindowCloseReason.OSShutdown, shuttingDown: false, hasWayBack: true));
    }

    /// desktop.Shutdown() closes windows with this reason — which is how the
    /// tray's Quit item and, more importantly, the self-updater's relaunch both
    /// end the process. Cancelling it meant the update helper waited forever
    /// for a process that was never going to exit.
    [Fact]
    public void ApplicationShutdownIsObeyed()
    {
        Assert.False(MainWindow.ShouldHideToTray(
            WindowCloseReason.ApplicationShutdown, shuttingDown: false, hasWayBack: true));
    }

    [Fact]
    public void AnExplicitQuitIsObeyed()
    {
        Assert.False(MainWindow.ShouldHideToTray(
            WindowCloseReason.WindowClosing, shuttingDown: true, hasWayBack: true));
    }
}
