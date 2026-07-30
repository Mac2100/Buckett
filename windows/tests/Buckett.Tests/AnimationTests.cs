using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Buckett.Views;
using Xunit;

namespace Buckett.Tests;

/// Avalonia has no animator for the RenderTransform property itself. Setters
/// have to target a transform property — TranslateTransform.Y,
/// ScaleTransform.ScaleX — while the animation still runs against the control,
/// which is what the transform animator expects: it reaches into the control's
/// RenderTransform on its own. Animating RenderTransform directly throws "No
/// animator registered for the property RenderTransform", and when the
/// animation lived in a style, it threw while the main window was being built,
/// killing the app before anything appeared on screen.
public class AnimationTests
{
    [AvaloniaFact]
    public void UpdateOverlayBobbingRunsAndStops()
    {
        var overlay = new UpdateOverlay();

        var failure = Record.Exception(() =>
        {
            overlay.StartBobbing();
            overlay.StopBobbing();
        });

        Assert.Null(failure);
    }

    [AvaloniaFact]
    public void BobbingIsIdempotent()
    {
        var overlay = new UpdateOverlay();

        var failure = Record.Exception(() =>
        {
            overlay.StartBobbing();
            overlay.StartBobbing();
            overlay.StopBobbing();
            overlay.StopBobbing();
        });

        Assert.Null(failure);
    }

    [AvaloniaFact]
    public void DropAnimationRuns()
    {
        var fileIcon = new Glyph { Symbol = "doc", Size = 30 };
        var bucket = new Border { Width = 52, Height = 52 };
        var caption = new TextBlock { Text = "Uploading 1 file" };

        var failure = Record.Exception(
            () => DropOverlays.RunAnimation(fileIcon, bucket, caption));

        Assert.Null(failure);
        Assert.NotNull(fileIcon.RenderTransform);
        Assert.NotNull(bucket.RenderTransform);
    }
}
