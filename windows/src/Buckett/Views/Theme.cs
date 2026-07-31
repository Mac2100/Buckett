using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Buckett.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Buckett.Views;

// MARK: - Themes

public sealed class AppTheme
{
    public AppTheme(string id, string name, Color primary, Color secondary)
    {
        Id = id;
        Name = name;
        Primary = primary;
        Secondary = secondary;
        PrimaryBrush = new SolidColorBrush(primary);
        SecondaryBrush = new SolidColorBrush(secondary);
        Gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(primary, 0),
                new GradientStop(secondary, 1)
            }
        };
    }

    public string Id { get; }
    public string Name { get; }
    public Color Primary { get; }
    public Color Secondary { get; }
    public IBrush PrimaryBrush { get; }
    public IBrush SecondaryBrush { get; }
    public IBrush Gradient { get; }

    public IBrush PrimaryWithOpacity(double opacity) =>
        new SolidColorBrush(Primary, opacity);

    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

    public static Color Rgb(double red, double green, double blue) =>
        Color.FromRgb(Channel(red), Channel(green), Channel(blue));
}

public static class Themes
{
    /// The same six accents as the macOS build, with identical RGB values.
    public static readonly IReadOnlyList<AppTheme> All = new[]
    {
        new AppTheme("bucket", "Bucket",
            AppTheme.Rgb(0.33, 0.29, 0.90), AppTheme.Rgb(0.05, 0.60, 0.55)),
        new AppTheme("ocean", "Ocean",
            AppTheme.Rgb(0.06, 0.42, 0.90), AppTheme.Rgb(0.15, 0.75, 0.90)),
        new AppTheme("sunset", "Sunset",
            AppTheme.Rgb(0.94, 0.42, 0.16), AppTheme.Rgb(0.90, 0.20, 0.50)),
        new AppTheme("forest", "Forest",
            AppTheme.Rgb(0.13, 0.55, 0.28), AppTheme.Rgb(0.35, 0.78, 0.55)),
        new AppTheme("grape", "Grape",
            AppTheme.Rgb(0.55, 0.27, 0.85), AppTheme.Rgb(0.88, 0.35, 0.65)),
        new AppTheme("graphite", "Graphite",
            AppTheme.Rgb(0.35, 0.37, 0.42), AppTheme.Rgb(0.55, 0.58, 0.64))
    };

    public static AppTheme Theme(string id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}

public enum AppearanceMode { System, Light, Dark }

public static class AppearanceModeExtensions
{
    public static string RawValue(this AppearanceMode mode) => mode switch
    {
        AppearanceMode.Light => "light",
        AppearanceMode.Dark => "dark",
        _ => "system"
    };

    public static AppearanceMode FromRawValue(string? raw) => raw switch
    {
        "light" => AppearanceMode.Light,
        "dark" => AppearanceMode.Dark,
        _ => AppearanceMode.System
    };

    public static string Label(this AppearanceMode mode) => mode switch
    {
        AppearanceMode.Light => "Light",
        AppearanceMode.Dark => "Dark",
        _ => "System"
    };

    public static ThemeVariant? Variant(this AppearanceMode mode) => mode switch
    {
        AppearanceMode.Light => ThemeVariant.Light,
        AppearanceMode.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };

    public static IReadOnlyList<AppearanceMode> All { get; } =
        new[] { AppearanceMode.System, AppearanceMode.Light, AppearanceMode.Dark };
}

/// Holds the selected accent theme and appearance override, and pushes both
/// into the application's resources so every view picks them up live.
public sealed class ThemeStore : ObservableObject
{
    public static ThemeStore Shared { get; } = new();

    private string _themeID;
    private AppearanceMode _appearance;

    private ThemeStore()
    {
        _themeID = Settings.Shared.ThemeID;
        _appearance = AppearanceModeExtensions.FromRawValue(Settings.Shared.AppearanceMode);
    }

    public string ThemeID
    {
        get => _themeID;
        set
        {
            if (_themeID == value) return;
            _themeID = value;
            Settings.Shared.ThemeID = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Theme));
            ApplyTheme();
        }
    }

    public AppearanceMode Appearance
    {
        get => _appearance;
        set
        {
            if (_appearance == value) return;
            _appearance = value;
            Settings.Shared.AppearanceMode = value.RawValue();
            OnPropertyChanged();
            ApplyAppearance();
        }
    }

    public AppTheme Theme => Themes.Theme(_themeID);

    public void ApplyAll()
    {
        ApplyTheme();
        ApplyAppearance();
    }

    public void ApplyAppearance()
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = Appearance.Variant();
        }
    }

    /// Publishes the accent as app-level resources. Fluent's own accent
    /// resources are overridden too, so stock controls tint with the theme.
    public void ApplyTheme()
    {
        if (Application.Current is not { } app) return;
        var theme = Theme;
        var resources = app.Resources;

        resources["ThemePrimaryColor"] = theme.Primary;
        resources["ThemeSecondaryColor"] = theme.Secondary;
        resources["ThemePrimaryBrush"] = theme.PrimaryBrush;
        resources["ThemeSecondaryBrush"] = theme.SecondaryBrush;
        resources["ThemeGradientBrush"] = theme.Gradient;
        resources["ThemePrimarySoftBrush"] = theme.PrimaryWithOpacity(0.16);
        resources["ThemePrimaryFaintBrush"] = theme.PrimaryWithOpacity(0.08);
        resources["ThemePrimaryEdgeBrush"] = theme.PrimaryWithOpacity(0.55);

        resources["SystemAccentColor"] = theme.Primary;
        resources["SystemAccentColorLight1"] = Lighten(theme.Primary, 0.18);
        resources["SystemAccentColorLight2"] = Lighten(theme.Primary, 0.34);
        resources["SystemAccentColorLight3"] = Lighten(theme.Primary, 0.5);
        resources["SystemAccentColorDark1"] = Darken(theme.Primary, 0.14);
        resources["SystemAccentColorDark2"] = Darken(theme.Primary, 0.28);
        resources["SystemAccentColorDark3"] = Darken(theme.Primary, 0.42);
    }

    private static Color Lighten(Color color, double amount) => Color.FromArgb(
        color.A,
        (byte)Math.Clamp(color.R + (255 - color.R) * amount, 0, 255),
        (byte)Math.Clamp(color.G + (255 - color.G) * amount, 0, 255),
        (byte)Math.Clamp(color.B + (255 - color.B) * amount, 0, 255));

    private static Color Darken(Color color, double amount) => Color.FromArgb(
        color.A,
        (byte)Math.Clamp(color.R * (1 - amount), 0, 255),
        (byte)Math.Clamp(color.G * (1 - amount), 0, 255),
        (byte)Math.Clamp(color.B * (1 - amount), 0, 255));
}

/// The app's identity mark: a gradient rounded square with the archive glyph —
/// used in the sidebar header, the welcome screen, the update overlay, and About.
public sealed class AppGlyph : Decorator
{
    public static readonly StyledProperty<double> GlyphSizeProperty =
        AvaloniaProperty.Register<AppGlyph, double>(nameof(GlyphSize), 28.0);

    public AppGlyph()
    {
        Rebuild();
    }

    public double GlyphSize
    {
        get => GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GlyphSizeProperty) Rebuild();
    }

    private void Rebuild()
    {
        var size = GlyphSize;
        var border = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.24),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Glyph
            {
                Symbol = "archivebox.fill",
                Size = size * 0.56,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = size * 0.34,
                OffsetY = size * 0.1,
                Color = Color.FromArgb(70, 0, 0, 0)
            })
        };
        border[!Border.BackgroundProperty] = new DynamicResourceExtension("ThemeGradientBrush");
        Child = border;
    }
}
