using System;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Buckett.Views;

/// Draws one icon from `Icons` at a given size, tinted with the inherited
/// foreground (or an explicit brush). The direct counterpart of SwiftUI's
/// `Image(systemName:)`.
public sealed class Glyph : Control
{
    public static readonly StyledProperty<string> SymbolProperty =
        AvaloniaProperty.Register<Glyph, string>(nameof(Symbol), "doc");

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<Glyph, double>(nameof(Size), 16.0);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Glyph>();

    /// Multiplies the authored stroke width, for heavier or lighter glyphs.
    public static readonly StyledProperty<double> WeightProperty =
        AvaloniaProperty.Register<Glyph, double>(nameof(Weight), 1.0);

    private static readonly ConcurrentDictionary<string, Geometry> Cache = new();

    static Glyph()
    {
        AffectsMeasure<Glyph>(SizeProperty);
        AffectsRender<Glyph>(SymbolProperty, SizeProperty, ForegroundProperty, WeightProperty);
    }

    public string Symbol
    {
        get => GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double Weight
    {
        get => GetValue(WeightProperty);
        set => SetValue(WeightProperty, value);
    }

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize) =>
        new(Size, Size);

    public override void Render(DrawingContext context)
    {
        var brush = Foreground ?? Brushes.Black;
        var icon = Icons.Get(Symbol);
        var scale = Size / Icons.DesignSize;
        if (scale <= 0) return;

        // Centre the 24x24 design box inside whatever space the layout gave us.
        var offsetX = (Bounds.Width - Size) / 2;
        var offsetY = (Bounds.Height - Size) / 2;

        using var _ = context.PushTransform(
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY));

        if (icon.Fill is { Length: > 0 } fill)
        {
            var geometry = Parse("F0 " + fill);
            if (geometry != null) context.DrawGeometry(brush, null, geometry);
        }

        if (icon.Stroke is { Length: > 0 } stroke)
        {
            var geometry = Parse(stroke);
            if (geometry != null)
            {
                var pen = new Pen(brush, Icons.StrokeWidth * Weight)
                {
                    LineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                context.DrawGeometry(null, pen, geometry);
            }
        }
    }

    private static Geometry? Parse(string data)
    {
        if (Cache.TryGetValue(data, out var cached)) return cached;
        try
        {
            var geometry = Geometry.Parse(data);
            Cache[data] = geometry;
            return geometry;
        }
        catch (Exception error)
        {
            Services.Log.Warn($"bad icon geometry: {error.Message}");
            return null;
        }
    }
}
