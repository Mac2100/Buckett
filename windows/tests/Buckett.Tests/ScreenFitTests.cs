using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Buckett.Views;
using Xunit;

namespace Buckett.Tests;

/// MainWindow asks for 1280x820 and centres itself on the screen. Avalonia
/// centres it even when it does not fit, splitting the overflow across both
/// edges — so on a 1280x800 laptop panel (1920x1200 at 150% scaling, the
/// commonest Windows laptop there is) the title bar landed above the top of the
/// display. The window looked deliberately chromeless and could not be moved,
/// resized or closed.
public class ScreenFitTests
{
    private static readonly Size Preferred = new(1280, 820);
    private static readonly Size Minimum = new(1000, 620);

    /// The reported case: 1280x800 panel, ~48px of taskbar, ~40px of frame.
    [Fact]
    public void ShrinksToFitALaptopPanel()
    {
        var (size, _) = ScreenFit.Fit(Preferred, Minimum, new Size(1278, 712));

        Assert.Equal(1278, size.Width);
        Assert.Equal(712, size.Height);
    }

    [Fact]
    public void LeavesAWindowThatAlreadyFitsAlone()
    {
        var (size, minimum) = ScreenFit.Fit(Preferred, Minimum, new Size(2558, 1400));

        Assert.Equal(Preferred, size);
        Assert.Equal(Minimum, minimum);
    }

    /// A minimum larger than the screen would make the window impossible to
    /// resize down to something usable, so the minimum is what gives way.
    [Fact]
    public void GivesUpTheMinimumBeforeOverflowingTheScreen()
    {
        var (size, minimum) = ScreenFit.Fit(Preferred, Minimum, new Size(900, 500));

        Assert.Equal(900, minimum.Width);
        Assert.Equal(500, minimum.Height);
        Assert.Equal(900, size.Width);
        Assert.Equal(500, size.Height);
    }

    /// Whatever the screen claims, never clamp the window out of existence.
    [Fact]
    public void NeverCollapsesTheWindow()
    {
        var (size, _) = ScreenFit.Fit(Preferred, Minimum, new Size(0, 0));

        Assert.True(size.Width >= 360);
        Assert.True(size.Height >= 240);
    }

    /// Windows that size themselves to their content leave Width or Height NaN;
    /// clamping must not pin them to a number they never asked for.
    [Fact]
    public void PreservesAutoSizing()
    {
        var (size, _) = ScreenFit.Fit(
            new Size(540, double.NaN), new Size(0, 0), new Size(1278, 712));

        Assert.Equal(540, size.Width);
        Assert.True(double.IsNaN(size.Height));
    }

    /// The visible symptom was a missing title bar, so assert the thing that
    /// was actually missing: nothing in the app may strip the window chrome.
    [AvaloniaFact]
    public void MainWindowKeepsItsTitleBar()
    {
        var window = new MainWindow();

        Assert.Equal(SystemDecorations.Full, window.SystemDecorations);
        Assert.True(window.CanResize);
        Assert.False(window.ExtendClientAreaToDecorationsHint);
    }

    /// Fitting runs from the constructor, so it has to cope with a window that
    /// has never been shown and has no frame to measure yet.
    [AvaloniaFact]
    public void FittingAnUnshownWindowDoesNotThrow()
    {
        var window = new Window { Width = 1280, Height = 820 };

        var failure = Record.Exception(() =>
        {
            window.FitToScreen();
            window.EnsureOnScreen();
        });

        Assert.Null(failure);
    }

    /// End to end against a real screen, rather than the arithmetic alone: a
    /// window too big for the display it opens on comes back inside it, frame
    /// included. Sized past any plausible test screen so the clamp has to bite.
    [AvaloniaFact]
    public void ClampsAWindowLargerThanItsScreen()
    {
        var window = new Window { Width = 8000, Height = 6000 };
        var work = window.Screens.Primary!.WorkingArea;
        var scaling = window.Screens.Primary!.Scaling;

        window.FitToScreen();

        Assert.True(window.Width < work.Width / scaling, $"width {window.Width}");
        Assert.True(window.Height < work.Height / scaling, $"height {window.Height}");
    }

    /// A window placed off the top of the screen — exactly what centring an
    /// oversized window did — gets pulled back so its title bar is reachable.
    [AvaloniaFact]
    public void PullsAWindowBackOntoTheScreen()
    {
        var window = new Window { Width = 600, Height = 400 };
        window.Show();
        window.Position = new PixelPoint(-400, -300);

        window.EnsureOnScreen();

        var work = window.Screens.Primary!.WorkingArea;
        Assert.True(window.Position.X >= work.X, $"x {window.Position.X}");
        Assert.True(window.Position.Y >= work.Y, $"y {window.Position.Y}");
    }
}
