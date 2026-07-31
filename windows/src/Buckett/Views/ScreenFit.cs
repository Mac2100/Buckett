using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Buckett.Services;

namespace Buckett.Views;

/// Avalonia will happily centre a window that is larger than the screen it is
/// centring on: the overflow is split between the two edges, so a window taller
/// than the desktop opens with its title bar above the top of the display. That
/// leaves nothing to grab — no close, no minimise, no drag — and the window
/// looks like it deliberately has no chrome. Windows never does this to a
/// native app, and neither does macOS, so the port has to clamp for itself.
///
/// A 1280x820 window is fine on a desktop monitor and impossible on a 1280x800
/// laptop panel, which is what 1920x1200 at 150% scaling comes to.
internal static class ScreenFit
{
    /// Smallest window worth opening, in device-independent pixels. Below this
    /// the content is unusable anyway, and it stops a nonsense screen report
    /// from collapsing the window to nothing.
    private static readonly Size Floor = new(360, 240);

    /// Assumed frame overhead before the window has been shown and can be
    /// measured: a Windows 11 title bar plus borders. Erring high only costs a
    /// few pixels of window on screens that had room to spare.
    private static readonly Size AssumedFrame = new(2, 40);

    /// The geometry on its own, so it can be tested without a display.
    /// <paramref name="available"/> is the working area already net of whatever
    /// the window frame costs.
    internal static (Size Size, Size Minimum) Fit(Size preferred, Size minimum, Size available)
    {
        var room = new Size(
            Math.Max(available.Width, Floor.Width),
            Math.Max(available.Height, Floor.Height));

        // A minimum bigger than the screen defeats the whole point: the window
        // could never be resized down far enough to fit. The minimum gives way
        // first — a cramped window beats an unreachable one.
        var minimum2 = new Size(
            Math.Min(minimum.Width, room.Width),
            Math.Min(minimum.Height, room.Height));

        return (new Size(
            Clamp(preferred.Width, minimum2.Width, room.Width),
            Clamp(preferred.Height, minimum2.Height, room.Height)), minimum2);
    }

    /// Width and Height are NaN on a window that sizes itself to its content;
    /// that has to survive the clamp, or the window is pinned to a size it
    /// never asked for.
    private static double Clamp(double preferred, double min, double max) =>
        double.IsNaN(preferred) ? preferred : Math.Clamp(preferred, min, max);

    /// Clamps the window now, and again once it opens — the frame, title bar
    /// included, can only be measured on a window that exists. One call from
    /// the constructor is all any window needs.
    internal static void KeepOnScreen(this Window window)
    {
        window.FitToScreen();
        window.Opened += (_, _) =>
        {
            window.FitToScreen();
            window.EnsureOnScreen();
        };
    }

    /// Shrinks the window to what its screen can actually show. Safe to call
    /// before the window is shown — the frame is estimated then, and measured
    /// afterwards.
    internal static void FitToScreen(this Window window)
    {
        try
        {
            if (ScreenOf(window) is not { } screen) return;

            var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
            var overhead = FrameOverhead(window);
            var available = new Size(
                screen.WorkingArea.Width / scaling - overhead.Width,
                screen.WorkingArea.Height / scaling - overhead.Height);

            var (size, minimum) = Fit(
                new Size(window.Width, window.Height),
                new Size(window.MinWidth, window.MinHeight),
                available);

            // Order matters: lower the floor before the size, or the old
            // minimum clamps the new size straight back up.
            window.MinWidth = minimum.Width;
            window.MinHeight = minimum.Height;
            if (!double.IsNaN(size.Width)) window.Width = size.Width;
            if (!double.IsNaN(size.Height)) window.Height = size.Height;
        }
        catch (Exception error)
        {
            // Never worth taking the app down over: an unclamped window is
            // still better than no window.
            Log.Warn($"could not fit window to screen: {error.Message}");
        }
    }

    /// Nudges an open window back inside the working area, so the title bar is
    /// reachable however the window came to be positioned. This is the
    /// backstop — it fixes up whatever the startup placement actually did,
    /// rather than predicting it.
    internal static void EnsureOnScreen(this Window window)
    {
        try
        {
            if (ScreenOf(window) is not { } screen) return;

            var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
            var frame = PixelSize.FromSize(window.FrameSize ?? window.ClientSize, scaling);
            var work = screen.WorkingArea;

            var position = window.Position;
            var x = Math.Clamp(position.X, work.X, Math.Max(work.X, work.Right - frame.Width));
            var y = Math.Clamp(position.Y, work.Y, Math.Max(work.Y, work.Bottom - frame.Height));

            if (x != position.X || y != position.Y) window.Position = new PixelPoint(x, y);
        }
        catch (Exception error)
        {
            Log.Warn($"could not move window on screen: {error.Message}");
        }
    }

    private static Screen? ScreenOf(Window window) =>
        window.Screens is not { } screens
            ? null
            : screens.ScreenFromWindow(window) ?? screens.Primary ?? screens.All.FirstOrDefault();

    private static Size FrameOverhead(Window window)
    {
        if (window.FrameSize is { } frame
            && frame.Width >= window.ClientSize.Width
            && frame.Height >= window.ClientSize.Height)
        {
            return new Size(
                frame.Width - window.ClientSize.Width,
                frame.Height - window.ClientSize.Height);
        }

        return AssumedFrame;
    }
}
