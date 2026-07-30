using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Buckett.Views;

public sealed record ChartBar(string Label, double Value, string ValueText);

/// Horizontal bar chart — the stand-in for the Swift Charts `BarMark` used by
/// the dashboard and statistics cards. Bars are tinted along the active theme
/// gradient so each category stays distinguishable.
public sealed class BarChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartBar>?> BarsProperty =
        AvaloniaProperty.Register<BarChart, IReadOnlyList<ChartBar>?>(nameof(Bars));

    public static readonly StyledProperty<double> RowHeightProperty =
        AvaloniaProperty.Register<BarChart, double>(nameof(RowHeight), 26.0);

    private const double LabelWidth = 74;
    private const double LabelGap = 8;
    private const double ValueLaneWidth = 76;

    static BarChart()
    {
        AffectsMeasure<BarChart>(BarsProperty, RowHeightProperty);
        AffectsRender<BarChart>(BarsProperty, RowHeightProperty);
    }

    public IReadOnlyList<ChartBar>? Bars
    {
        get => GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Bars?.Count ?? 0;
        return new Size(
            double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width,
            Math.Max(RowHeight, count * RowHeight));
    }

    public override void Render(DrawingContext context)
    {
        var bars = Bars;
        if (bars == null || bars.Count == 0) return;

        var theme = ThemeStore.Shared.Theme;
        var labelBrush = this.FindResource("SecondaryTextBrush") as IBrush ?? Brushes.Gray;
        var trackBrush = this.FindResource("FaintFillBrush") as IBrush ?? Brushes.LightGray;

        var max = bars.Max(bar => bar.Value);
        if (max <= 0) max = 1;

        var chartLeft = LabelWidth + LabelGap;
        // Keep a lane on the right for the value labels so a full-width bar
        // never runs underneath its own caption.
        var chartWidth = Math.Max(20, Bounds.Width - chartLeft - ValueLaneWidth);
        var barHeight = Math.Min(14, RowHeight - 8);

        for (var index = 0; index < bars.Count; index++)
        {
            var bar = bars[index];
            var top = index * RowHeight;
            var centre = top + RowHeight / 2;

            var label = new FormattedText(
                Truncate(bar.Label, 12),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                11.5,
                labelBrush);
            context.DrawText(label, new Point(
                Math.Max(0, LabelWidth - label.Width),
                centre - label.Height / 2));

            var track = new RoundedRect(
                new Rect(chartLeft, centre - barHeight / 2, chartWidth, barHeight),
                barHeight / 2);
            context.DrawRectangle(trackBrush, null, track);

            var fraction = Math.Clamp(bar.Value / max, 0, 1);
            var width = Math.Max(barHeight, chartWidth * fraction);
            var fill = new SolidColorBrush(Blend(
                theme.Primary, theme.Secondary,
                bars.Count == 1 ? 0 : (double)index / (bars.Count - 1)));
            var filled = new RoundedRect(
                new Rect(chartLeft, centre - barHeight / 2, width, barHeight),
                barHeight / 2);
            context.DrawRectangle(fill, null, filled);

            var value = new FormattedText(
                bar.ValueText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                10.5,
                labelBrush);
            context.DrawText(
                value,
                new Point(chartLeft + width + 6, centre - value.Height / 2));
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    private static Color Blend(Color from, Color to, double t) => Color.FromArgb(
        255,
        (byte)(from.R + (to.R - from.R) * t),
        (byte)(from.G + (to.G - from.G) * t),
        (byte)(from.B + (to.B - from.B) * t));
}
