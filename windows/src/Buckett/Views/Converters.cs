using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Buckett.Views;

public static class Converters
{
    /// Inverts a boolean — for "IsVisible" bindings that read better negated.
    public static readonly IValueConverter Not =
        new FuncValueConverter<bool, bool>(value => !value);

    public static readonly IValueConverter IsNotNull =
        new FuncValueConverter<object?, bool>(value => value != null);

    public static readonly IValueConverter IsNull =
        new FuncValueConverter<object?, bool>(value => value == null);

    public static readonly IValueConverter IsNotEmpty =
        new FuncValueConverter<string?, bool>(value => !string.IsNullOrEmpty(value));

    public static readonly IValueConverter IsEmpty =
        new FuncValueConverter<string?, bool>(string.IsNullOrEmpty);

    public static readonly IValueConverter IsPositive =
        new FuncValueConverter<int, bool>(value => value > 0);

    public static readonly IValueConverter IsZero =
        new FuncValueConverter<int, bool>(value => value == 0);

    /// Looks up a brush by resource key, so view models can name a colour
    /// without referencing the visual tree.
    public static readonly IValueConverter BrushForKey = new BrushKeyConverter();

    /// True when the bound value equals the converter parameter — used for
    /// segmented controls and tab selection.
    public static readonly IValueConverter EqualsParameter = new EqualityConverter();

    private sealed class BrushKeyConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string key) return Brushes.Gray;
            var variant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Default;
            if (Application.Current?.TryGetResource(key, variant, out var resource) == true &&
                resource is IBrush brush)
            {
                return brush;
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class EqualityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Equals(value?.ToString(), parameter?.ToString());

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
