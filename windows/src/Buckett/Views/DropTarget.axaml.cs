using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Buckett.Models;
using Buckett.Services;
using Buckett.ViewModels;

namespace Buckett.Views;

/// The Windows stand-in for the macOS menu bar drop target: a small, always-on-top
/// bucket you can park anywhere on the desktop and drop files onto. (Notification-area
/// icons on Windows cannot receive dropped files, so the target is a real window.)
public partial class DropPadWindow : Window
{
    private readonly AppState _state = AppState.Shared;

    public DropPadWindow()
    {
        InitializeComponent();

        PadGlyph.Symbol = Settings.Shared.TrayIconStyle;
        Settings.Shared.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Settings.TrayIconStyle))
            {
                Dispatcher.UIThread.Post(() => PadGlyph.Symbol = Settings.Shared.TrayIconStyle);
            }
        };

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        Pad.PointerPressed += OnPadPressed;
        RestorePosition();
    }

    private void RestorePosition()
    {
        var x = Settings.Shared.DropTargetX;
        var y = Settings.Shared.DropTargetY;
        if (x >= 0 && y >= 0)
        {
            Position = new PixelPoint((int)x, (int)y);
            return;
        }

        // Default: bottom-right of the primary work area, clear of the taskbar.
        var screen = Screens.Primary;
        if (screen == null) return;
        var area = screen.WorkingArea;
        Position = new PixelPoint(area.X + area.Width - 130, area.Y + area.Height - 130);
    }

    private void OnPadPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            ShowMenu();
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;

        if (e.ClickCount >= 2)
        {
            _state.OpenMainWindow();
            return;
        }
        BeginMoveDrag(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Settings.Shared.DropTargetX = Position.X;
        Settings.Shared.DropTargetY = Position.Y;
        base.OnClosing(e);
    }

    private void ShowMenu()
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open Buckett" };
        open.Click += (_, _) => _state.OpenMainWindow();
        menu.Items.Add(open);

        var hide = new MenuItem { Header = "Hide Drop Target" };
        hide.Click += (_, _) => Settings.Shared.ShowDropTarget = false;
        menu.Items.Add(hide);

        menu.Open(Pad);
    }

    // MARK: - Drag & drop

    private static IReadOnlyList<string> PathsFrom(DragEventArgs e) =>
        e.Data.GetFiles()?
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList()
        ?? (IReadOnlyList<string>)Array.Empty<string>();

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        Highlight.IsVisible = true;
        e.DragEffects = DragDropEffects.Copy;
        DropOverlays.Shared.ShowHover(this);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDragLeave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Highlight.IsVisible = false;
        // The drag may be travelling into the hover panel, so close on a grace period.
        DropOverlays.Shared.ScheduleHoverClose(TimeSpan.FromSeconds(0.9));
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        Highlight.IsVisible = false;
        var paths = PathsFrom(e);
        if (paths.Count == 0) return;
        DropOverlays.Shared.HandleDrop(paths, target: null, near: this);
    }
}

/// Owns the transient windows that surround the drop target: the hover list of
/// bucket drop zones and the "file falls into the bucket" animation.
public sealed class DropOverlays
{
    public static DropOverlays Shared { get; } = new();

    private Window? _hover;
    private Window? _animation;
    private CancellationTokenSource? _hoverClose;

    private DropOverlays() { }

    public void ShowHover(DropPadWindow pad)
    {
        CancelHoverClose();
        if (_hover != null) return;

        var rows = AppState.Shared.DropRows();
        if (rows.Count == 0) return;

        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock
        {
            Text = rows.Count == 1 ? "Release to upload" : "Drop on a bucket",
            Classes = { "callout" },
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        foreach (var row in rows)
        {
            content.Children.Add(BuildRow(row, pad));
        }

        var panel = new Border
        {
            Classes = { "card" },
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12),
            Child = content
        };

        var window = TransientWindow(240, 66 + rows.Count * 52);
        window.Content = panel;
        PlaceNear(window, pad, above: true);
        window.Show();
        _hover = window;
    }

    private Control BuildRow(DropRow row, DropPadWindow pad)
    {
        var border = new Border
        {
            Classes = { "rowCard" },
            Padding = new Thickness(10, 9),
            Width = 214
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var icon = new Glyph { Symbol = "tray.full.fill", Size = 15, Margin = new Thickness(0, 0, 8, 0) };
        icon[!Glyph.ForegroundProperty] = new DynamicResourceExtension("SecondaryTextBrush");
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = row.DisplayName,
            Classes = { "callout" },
            FontWeight = FontWeight.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        labels.Children.Add(new TextBlock
        {
            Text = row.AccountName,
            Classes = { "caption2", "secondary" },
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(labels, 1);
        grid.Children.Add(labels);

        var arrow = new Glyph { Symbol = "arrow.down.circle", Size = 15 };
        arrow[!Glyph.ForegroundProperty] = new DynamicResourceExtension("SecondaryTextBrush");
        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);

        border.Child = grid;

        DragDrop.SetAllowDrop(border, true);
        border.AddHandler(DragDrop.DragEnterEvent, (_, e) =>
        {
            if (!e.Data.Contains(DataFormats.Files)) return;
            border.Classes.Add("selected");
            arrow.Symbol = "arrow.down.circle.fill";
            e.DragEffects = DragDropEffects.Copy;
            CancelHoverClose();
        });
        border.AddHandler(DragDrop.DragLeaveEvent, (_, _) =>
        {
            border.Classes.Remove("selected");
            arrow.Symbol = "arrow.down.circle";
            ScheduleHoverClose(TimeSpan.FromSeconds(0.9));
        });
        border.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            border.Classes.Remove("selected");
            arrow.Symbol = "arrow.down.circle";
            var paths = e.Data.GetFiles()?
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(path => path!)
                .ToList() ?? new List<string>();
            if (paths.Count == 0) return;
            HandleDrop(paths, row.Target, pad);
        });

        return border;
    }

    public void HandleDrop(IReadOnlyList<string> paths, DropTarget? target, Window near)
    {
        CloseHover();
        var resolved = AppState.Shared.HandleDrop(paths, target);
        if (resolved == null) return;

        var display = BucketAliases.Shared.DisplayName(resolved.Value.AccountID, resolved.Value.Bucket);
        PlayAnimation(paths, display, near);
    }

    public void CancelHoverClose()
    {
        _hoverClose?.Cancel();
        _hoverClose?.Dispose();
        _hoverClose = null;
    }

    /// Closes the hover panel after a grace period, so a drag can travel from
    /// the pad down into the panel without it vanishing mid-way.
    public void ScheduleHoverClose(TimeSpan delay)
    {
        CancelHoverClose();
        var cancellation = new CancellationTokenSource();
        _hoverClose = cancellation;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cancellation.Token).ConfigureAwait(true);
                await Dispatcher.UIThread.InvokeAsync(CloseHover);
            }
            catch (OperationCanceledException)
            {
                // The drag came back.
            }
        });
    }

    public void CloseHover()
    {
        CancelHoverClose();
        _hover?.Close();
        _hover = null;
    }

    /// The dropped file's icon falls into the bucket, the bucket squashes on
    /// impact, and an upload note fades in — mirroring the macOS animation.
    private void PlayAnimation(IReadOnlyList<string> paths, string bucket, Window near)
    {
        _animation?.Close();
        _animation = null;

        var theme = ThemeStore.Shared.Theme;
        var count = paths.Count;
        var extension = System.IO.Path.GetExtension(paths[0]).TrimStart('.').ToLowerInvariant();
        var fileSymbol = FileKinds.IsImage(extension) ? "photo"
            : FileKinds.IsVideo(extension) ? "film"
            : FileKinds.IsAudio(extension) ? "waveform"
            : FileKinds.IsArchive(extension) ? "doc.zipper"
            : FileKinds.IsText(extension) ? "doc.text"
            : "doc";

        var bucketBadge = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(13),
            Background = theme.Gradient,
            RenderTransformOrigin = RelativePoint.Parse("50%,100%"),
            Child = new Glyph
            {
                Symbol = Settings.Shared.TrayIconStyle,
                Size = 25,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var fileIcon = new Glyph
        {
            Symbol = fileSymbol,
            Size = 30,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0)
        };
        fileIcon[!Glyph.ForegroundProperty] = new DynamicResourceExtension("PrimaryTextBrush");

        var stage = new Panel { Height = 84 };
        stage.Children.Add(bucketBadge);
        stage.Children.Add(fileIcon);

        var caption = new StackPanel { Spacing = 1, Opacity = 0 };
        caption.Children.Add(new TextBlock
        {
            Text = $"Uploading {count} file{(count == 1 ? "" : "s")}",
            Classes = { "callout" },
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        caption.Children.Add(new TextBlock
        {
            Text = $"→ {bucket}",
            Classes = { "caption", "secondary" },
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(stage);
        content.Children.Add(caption);

        var card = new Border
        {
            Classes = { "card" },
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Child = content
        };

        var window = TransientWindow(210, 150);
        window.Content = card;
        PlaceNear(window, near, above: true);
        window.Show();
        _animation = window;

        RunAnimation(fileIcon, bucketBadge, caption);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(2100)).ConfigureAwait(true);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ReferenceEquals(_animation, window)) return;
                window.Close();
                _animation = null;
            });
        });
    }

    internal static void RunAnimation(Glyph fileIcon, Border bucketBadge, Control caption)
    {
        // Animate TranslateTransform.Y / ScaleTransform.Scale*, never
        // RenderTransform itself — only the former have registered animators.
        // The animation is applied to the control; Avalonia's transform animator
        // reaches into the RenderTransform assigned here.
        fileIcon.RenderTransform = new TranslateTransform();

        // The file falls in…
        var fall = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(400),
            Easing = new QuadraticEaseIn(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(TranslateTransform.YProperty, 0.0) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(TranslateTransform.YProperty, 42.0) }
                }
            }
        };
        _ = fall.RunAsync(fileIcon);

        // …fading out as it goes…
        var fadeOut = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(400),
            Easing = new QuadraticEaseIn(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.0) } }
            }
        };
        _ = fadeOut.RunAsync(fileIcon);

        // …the bucket squashes on impact and springs back…
        bucketBadge.RenderTransform = new ScaleTransform();

        var impact = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(360),
            Delay = TimeSpan.FromMilliseconds(380),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(ScaleTransform.ScaleXProperty, 1.0),
                        new Setter(ScaleTransform.ScaleYProperty, 1.0)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.3),
                    Setters =
                    {
                        new Setter(ScaleTransform.ScaleXProperty, 1.14),
                        new Setter(ScaleTransform.ScaleYProperty, 0.82)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(ScaleTransform.ScaleXProperty, 1.0),
                        new Setter(ScaleTransform.ScaleYProperty, 1.0)
                    }
                }
            }
        };
        _ = impact.RunAsync(bucketBadge);

        // …and the label fades in.
        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(250),
            Delay = TimeSpan.FromMilliseconds(500),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0) } }
            }
        };
        _ = fade.RunAsync(caption);
    }

    private static Window TransientWindow(double width, double height)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            SystemDecorations = SystemDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            CanResize = false,
            ShowActivated = false
        };
        window[!TopLevel.RequestedThemeVariantProperty] = new Avalonia.Data.Binding
        {
            Source = Application.Current,
            Path = nameof(Application.RequestedThemeVariant)
        };
        DragDrop.SetAllowDrop(window, true);
        return window;
    }

    private static void PlaceNear(Window window, Window anchor, bool above)
    {
        var x = anchor.Position.X + (int)(anchor.Width / 2) - (int)(window.Width / 2);
        var y = above
            ? anchor.Position.Y - (int)window.Height - 8
            : anchor.Position.Y + (int)anchor.Height + 8;

        if (anchor.Screens.ScreenFromWindow(anchor) is { } screen)
        {
            var area = screen.WorkingArea;
            x = Math.Clamp(x, area.X + 4, area.X + area.Width - (int)window.Width - 4);
            if (y < area.Y + 4) y = anchor.Position.Y + (int)anchor.Height + 8;
        }
        window.Position = new PixelPoint(x, y);
    }
}
