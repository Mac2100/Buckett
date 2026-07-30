using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Buckett.Models;
using Buckett.Services;
using Buckett.ViewModels;

namespace Buckett.Views;

/// Shared chrome for the small modal dialogs — the Windows counterpart of the
/// SwiftUI sheets: a centred, non-resizable window owned by its parent.
internal static class DialogChrome
{
    public static Window Create(string title, double width = 400)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window[!TopLevel.RequestedThemeVariantProperty] = new Avalonia.Data.Binding
        {
            Source = Application.Current,
            Path = nameof(Application.RequestedThemeVariant)
        };
        window[!TemplatedControl.BackgroundProperty] =
            new DynamicResourceExtension("WindowBackgroundBrush");
        try
        {
            window.Icon = new WindowIcon(
                Avalonia.Platform.AssetLoader.Open(new Uri("avares://Buckett/Assets/app.ico")));
        }
        catch (Exception error)
        {
            Log.Warn($"dialog icon unavailable: {error.Message}");
        }
        return window;
    }

    public static Glyph Icon(string symbol, double size, string brushKey = "ThemePrimaryBrush")
    {
        var glyph = new Glyph
        {
            Symbol = symbol,
            Size = size,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        glyph[!Glyph.ForegroundProperty] = new DynamicResourceExtension(brushKey);
        return glyph;
    }

    public static TextBlock Title(string text) => new()
    {
        Text = text,
        Classes = { "title3" },
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center
    };

    public static TextBlock Caption(string text) => new()
    {
        Text = text,
        Classes = { "caption", "secondary" },
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center
    };

    public static Button Prominent(string text) => new()
    {
        Content = text,
        Classes = { "prominent" },
        MinWidth = 92,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        IsDefault = true
    };

    public static Button Destructive(string text) => new()
    {
        Content = text,
        Classes = { "destructive" },
        MinWidth = 92,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    public static Button Cancel(string text = "Cancel") => new()
    {
        Content = text,
        MinWidth = 92,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        IsCancel = true
    };

    /// Left/right button row used at the bottom of every dialog.
    public static Grid ButtonRow(Control? left, params Control[] right)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 6, 0, 0)
        };
        if (left != null)
        {
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);
        }
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        foreach (var control in right) stack.Children.Add(control);
        Grid.SetColumn(stack, 2);
        grid.Children.Add(stack);
        return grid;
    }

    public static StackPanel Body(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(22) };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    public static TextBox Field(string watermark, string text = "")
    {
        return new TextBox { Watermark = watermark, Text = text };
    }
}

/// Yes/no confirmation and plain alerts.
public static class ConfirmWindow
{
    public static async Task<bool> AskAsync(
        Window owner,
        string title,
        string message,
        string confirmText,
        bool destructive = false)
    {
        var window = DialogChrome.Create(title);
        var result = false;

        var confirm = destructive
            ? DialogChrome.Destructive(confirmText)
            : DialogChrome.Prominent(confirmText);
        confirm.Click += (_, _) => { result = true; window.Close(); };

        var cancel = DialogChrome.Cancel();
        cancel.Click += (_, _) => window.Close();

        window.Content = DialogChrome.Body(
            DialogChrome.Icon(
                destructive ? "exclamationmark.triangle.fill" : "info.circle.fill",
                30,
                destructive ? "DangerBrush" : "ThemePrimaryBrush"),
            new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            },
            DialogChrome.ButtonRow(null, cancel, confirm));

        await window.ShowDialog(owner);
        return result;
    }

    public static async Task AlertAsync(Window owner, string title, string message)
    {
        var window = DialogChrome.Create(title);
        var ok = DialogChrome.Prominent("OK");
        ok.Click += (_, _) => window.Close();

        window.Content = DialogChrome.Body(
            DialogChrome.Icon("exclamationmark.triangle.fill", 30, "WarningBrush"),
            new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 340
            },
            DialogChrome.ButtonRow(null, ok));

        await window.ShowDialog(owner);
    }
}

public static class NewFolderWindow
{
    public static async Task ShowAsync(Window owner, BrowserModel model)
    {
        var window = DialogChrome.Create("New Folder", 360);
        var field = DialogChrome.Field("Folder name");

        var create = DialogChrome.Prominent("Create");
        create.IsEnabled = false;
        create.Click += (_, _) =>
        {
            var name = field.Text ?? "";
            window.Close();
            _ = model.CreateFolderAsync(name);
        };
        field.TextChanged += (_, _) => create.IsEnabled = (field.Text ?? "").Trim().Length > 0;

        var cancel = DialogChrome.Cancel();
        cancel.Click += (_, _) => window.Close();

        window.Content = DialogChrome.Body(
            DialogChrome.Icon("folder.badge.plus", 28),
            DialogChrome.Title("New Folder"),
            field,
            DialogChrome.ButtonRow(null, cancel, create));

        window.Opened += (_, _) => field.Focus();
        await window.ShowDialog(owner);
    }
}

public static class RenameWindow
{
    public static async Task ShowAsync(Window owner, BrowserModel model, RemoteObject target)
    {
        var window = DialogChrome.Create($"Rename {(target.IsFolder ? "Folder" : "File")}", 400);
        var field = DialogChrome.Field("Name", target.Name);

        var rename = DialogChrome.Prominent("Rename");
        rename.Click += (_, _) =>
        {
            var name = field.Text ?? "";
            window.Close();
            _ = model.RenameAsync(target, name);
        };

        void Validate()
        {
            var value = (field.Text ?? "").Trim();
            rename.IsEnabled = value.Length > 0 && value != target.Name && !value.Contains('/');
        }
        field.TextChanged += (_, _) => Validate();
        Validate();

        var cancel = DialogChrome.Cancel();
        cancel.Click += (_, _) => window.Close();

        var body = DialogChrome.Body(
            DialogChrome.Title($"Rename {(target.IsFolder ? "Folder" : "File")}"),
            field);

        if (target.IsFolder)
        {
            body.Children.Add(DialogChrome.Caption(
                "Renaming a folder copies every object under it to the new prefix."));
        }
        body.Children.Add(DialogChrome.ButtonRow(null, cancel, rename));

        window.Content = body;
        window.Opened += (_, _) => { field.Focus(); field.SelectAll(); };
        await window.ShowDialog(owner);
    }
}

public static class BatchRenameWindow
{
    public static async Task ShowAsync(Window owner, BrowserModel model)
    {
        var window = DialogChrome.Create("Batch Rename", 440);
        var find = DialogChrome.Field("Find");
        var replace = DialogChrome.Field("Replace with");

        var previewList = new ItemsControl { Margin = new Thickness(0, 2, 0, 0) };
        var previewScroller = new ScrollViewer
        {
            MaxHeight = 150,
            Content = previewList,
            IsVisible = false
        };
        var previewHeading = new TextBlock
        {
            Text = "Preview",
            Classes = { "caption", "secondary" },
            FontWeight = FontWeight.SemiBold,
            IsVisible = false
        };

        var rename = DialogChrome.Prominent("Rename");
        rename.IsEnabled = false;

        void UpdatePreview()
        {
            var findText = find.Text ?? "";
            var replaceText = replace.Text ?? "";
            var affected = findText.Length == 0
                ? new System.Collections.Generic.List<(RemoteObject Item, string NewName)>()
                : model.SelectedObjects
                    .Where(item => !item.IsFolder)
                    .Select(item => (Item: item, NewName: item.Name.Replace(findText, replaceText)))
                    .Where(pair => pair.NewName != pair.Item.Name
                                   && pair.NewName.Length > 0
                                   && !pair.NewName.Contains('/'))
                    .ToList();

            previewList.ItemsSource = affected
                .Take(20)
                .Select(pair => new TextBlock
                {
                    Text = $"{pair.Item.Name}  →  {pair.NewName}",
                    Classes = { "caption", "mono" },
                    TextTrimming = TextTrimming.CharacterEllipsis
                })
                .Concat(affected.Count > 20
                    ? new[]
                    {
                        new TextBlock
                        {
                            Text = $"…and {affected.Count - 20} more",
                            Classes = { "caption", "secondary" }
                        }
                    }
                    : Array.Empty<TextBlock>())
                .ToList();

            previewScroller.IsVisible = affected.Count > 0;
            previewHeading.IsVisible = affected.Count > 0;
            rename.IsEnabled = affected.Count > 0;
            rename.Content = $"Rename {affected.Count} File{(affected.Count == 1 ? "" : "s")}";
        }

        find.TextChanged += (_, _) => UpdatePreview();
        replace.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        rename.Click += (_, _) =>
        {
            var findText = find.Text ?? "";
            var replaceText = replace.Text ?? "";
            window.Close();
            _ = model.BatchRenameAsync(findText, replaceText);
        };

        var cancel = DialogChrome.Cancel();
        cancel.Click += (_, _) => window.Close();

        window.Content = DialogChrome.Body(
            DialogChrome.Title("Batch Rename"),
            DialogChrome.Caption("Find and replace within the names of the selected files."),
            find,
            replace,
            previewHeading,
            previewScroller,
            DialogChrome.ButtonRow(null, cancel, rename));

        window.Opened += (_, _) => find.Focus();
        await window.ShowDialog(owner);
    }
}

public static class AliasWindow
{
    public static void Show(Window owner, Guid accountID, string bucket)
    {
        var window = DialogChrome.Create("Bucket Alias", 380);
        var current = BucketAliases.Shared.Alias(accountID, bucket);
        var field = DialogChrome.Field("Alias (e.g. Backups)", current ?? "");

        var save = DialogChrome.Prominent("Save");
        save.Click += (_, _) =>
        {
            var alias = field.Text ?? "";
            BucketAliases.Shared.SetAlias(alias, accountID, bucket);
            window.Close();
            ToastCenter.Shared.Show(
                "Alias saved",
                alias.Trim().Length == 0 ? null : $"{bucket} → {alias}");
        };

        var cancel = DialogChrome.Cancel();
        cancel.Click += (_, _) => window.Close();

        Control? remove = null;
        if (current != null)
        {
            var removeButton = DialogChrome.Destructive("Remove");
            removeButton.Click += (_, _) =>
            {
                BucketAliases.Shared.SetAlias(null, accountID, bucket);
                window.Close();
            };
            remove = removeButton;
        }

        window.Content = DialogChrome.Body(
            DialogChrome.Icon("tag.fill", 26),
            DialogChrome.Title("Bucket Alias"),
            DialogChrome.Caption(
                $"Shown instead of “{bucket}” across Buckett.\n" +
                "Uploads and API requests still use the real bucket name."),
            field,
            DialogChrome.ButtonRow(remove, cancel, save));

        window.Opened += (_, _) => { field.Focus(); field.SelectAll(); };
        _ = window.ShowDialog(owner);
    }
}

public static class UpdateAvailableWindow
{
    public static void Show(Window owner, string version, string url)
    {
        var window = DialogChrome.Create("Update Available", 420);

        var install = DialogChrome.Prominent("Install & Relaunch");
        install.MinWidth = 150;
        install.Click += (_, _) =>
        {
            window.Close();
            SelfUpdater.Shared.Install(url);
        };

        var later = DialogChrome.Cancel("Later");
        later.Click += (_, _) => window.Close();

        window.Content = DialogChrome.Body(
            DialogChrome.Icon("arrow.down.circle.fill", 32),
            DialogChrome.Title("Update Available"),
            new TextBlock
            {
                Text = $"Buckett {version} is available. You are running {Support.AppVersion.Current}. " +
                       "Install now and the app will relaunch into the new version.",
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Classes = { "callout" }
            },
            DialogChrome.ButtonRow(null, later, install));

        _ = window.ShowDialog(owner);
    }
}
