using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Buckett.Models;
using Buckett.ViewModels;

namespace Buckett.Views;

public sealed record MetadataRow(string Label, string Value);

public partial class MetadataWindow : Window
{
    private readonly RemoteObject _object;
    private readonly BrowserModel _model;

    public MetadataWindow(RemoteObject remote, BrowserModel model)
    {
        _object = remote;
        _model = model;
        InitializeComponent();
        this.KeepOnScreen();
        _ = LoadAsync();
    }

    public static void Show(Window owner, RemoteObject remote, BrowserModel model)
    {
        var window = new MetadataWindow(remote, model);
        _ = window.ShowDialog(owner);
    }

    private async Task LoadAsync()
    {
        var metadata = await _model.MetadataAsync(_object);

        var rows = new List<MetadataRow>
        {
            new("Name", _object.Name),
            new("Key", _object.Key),
            new("Size", metadata?.ContentLength is { } length
                ? ByteFormat.String(length)
                : _object.FormattedSize),
            new("Content Type", metadata?.ContentType ?? "—"),
            new("Last Modified", metadata?.LastModified
                ?? _object.LastModified?.ToLocalTime().ToString("f") ?? "—"),
            new("ETag", metadata?.ETag ?? _object.ETag ?? "—"),
            new("Storage Class", metadata?.StorageClass ?? _object.StorageClass ?? "Standard")
        };

        if (metadata != null && metadata.Custom.Count > 0)
        {
            rows.Add(new MetadataRow(
                "Custom Metadata",
                string.Join("\n", metadata.Custom
                    .OrderBy(entry => entry.Key)
                    .Select(entry => $"{entry.Key}: {entry.Value}"))));
        }

        Host.ItemsSource = rows;
        Spinner.IsVisible = false;
        Scroller.IsVisible = true;
    }

    private void OnDone(object? sender, RoutedEventArgs e) => Close();
}
