using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Buckett.Models;
using Buckett.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Buckett.ViewModels;

/// One row/tile in the object browser: the remote object plus the presentation
/// state the views bind to (selection, thumbnail, formatted captions).
public sealed class ObjectItem : ObservableObject
{
    public ObjectItem(RemoteObject remote, string bucket, S3Client client)
    {
        Object = remote;
        Bucket = bucket;
        Client = client;
    }

    public RemoteObject Object { get; }
    public string Bucket { get; }
    public S3Client Client { get; }

    private bool _isSelected;
    private Bitmap? _thumbnail;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (SetProperty(ref _thumbnail, value))
            {
                OnPropertyChanged(nameof(HasThumbnail));
                OnPropertyChanged(nameof(ShowSymbol));
            }
        }
    }

    public bool HasThumbnail => _thumbnail != null;
    public bool ShowSymbol => _thumbnail == null;

    public string Id => Object.Id;
    public string Key => Object.Key;
    public string Name => Object.Name;
    public bool IsFolder => Object.IsFolder;
    public long Size => Object.Size;
    public DateTime SortDate => Object.SortDate;
    public string SymbolName => Object.SymbolName;
    public string FormattedSize => Object.FormattedSize;
    public bool CanHaveThumbnail => Object.IsImage;

    public bool ShowSizeBadge => !Object.IsFolder && Object.Size > 0;

    public string Subtitle
    {
        get
        {
            if (Object.IsFolder) return "Folder";
            return Object.LastModified is { } date
                ? $"{Object.FormattedSize} · {date.ToLocalTime():g}"
                : Object.FormattedSize;
        }
    }

    public string ModifiedText =>
        Object.LastModified is { } date ? date.ToLocalTime().ToString("d MMM yyyy, HH:mm") : "—";

    public string KindLabel => Object.IsFolder
        ? "Folder"
        : Object.FileExtension.Length == 0 ? "File" : Object.FileExtension.ToUpperInvariant();

    public string HoverLabel => Object.IsFolder ? "Open folder" : "Preview";

    /// Folders pick up the accent, files stay neutral — as on macOS.
    public IBrush SymbolBrush => Application.Current?.FindResource(
        Object.IsFolder ? "ThemePrimaryBrush" : "SecondaryTextBrush") as IBrush ?? Brushes.Gray;
}
