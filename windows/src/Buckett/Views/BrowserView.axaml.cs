using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Buckett.Models;
using Buckett.Services;
using Buckett.ViewModels;

namespace Buckett.Views;

public sealed record BreadcrumbItem(string Title, string Prefix, bool ShowChevron, FontWeight Weight);

public partial class BrowserView : UserControl
{
    private readonly BrowserModel _model;
    private readonly ObservableCollection<BreadcrumbItem> _breadcrumbs = new();
    private ObjectItem? _selectionAnchor;
    private bool _syncingListSelection;
    private bool _syncingFilter;

    public BrowserView(string bucket, S3Client client, TransferManager transfers)
    {
        _model = new BrowserModel(bucket, client, transfers);

        InitializeComponent();

        GridHost.ItemsSource = _model.Items;
        ListHost.ItemsSource = _model.Items;
        Breadcrumbs.ItemsSource = _breadcrumbs;

        _model.PropertyChanged += OnModelChanged;
        _model.ItemsChanged += OnItemsChanged;
        _model.SelectionChanged += SyncSelectionUi;
        transfers.PropertyChanged += (_, _) => Dispatcher.UIThread.Post(SyncStatusBar);

        FilterBox.TextChanged += OnFilterChanged;
        ListHost.SelectionChanged += OnListSelectionChanged;
        ListHost.Sorting += OnListSorting;

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        Sync();
        _ = _model.LoadAsync();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _model.Dispose();
    }

    // MARK: - Sync

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserModel.ErrorMessage) && _model.HasError)
        {
            var message = _model.ErrorMessage ?? "";
            _model.ErrorMessage = null;
            _ = ShowErrorAsync(message);
            return;
        }
        Sync();
    }

    private void OnItemsChanged()
    {
        Sync();
        LoadThumbnails();
    }

    private void Sync()
    {
        UpButton.IsEnabled = _model.CanNavigateUp;
        RebuildBreadcrumbs();

        GridToggle.IsChecked = _model.IsGrid;
        ListToggle.IsChecked = _model.IsList;

        var hasItems = !_model.IsEmpty;
        GridScroller.IsVisible = _model.IsGrid && hasItems;
        ListHost.IsVisible = _model.IsList && hasItems;
        LoadingBlock.IsVisible = _model.ShowLoadingPlaceholder;
        EmptyBlock.IsVisible = !hasItems && !_model.ShowLoadingPlaceholder;

        EmptyGlyph.Symbol = _model.HasFilter ? "magnifyingglass" : "folder";
        EmptyTitle.Text = _model.HasFilter ? "No matches" : "This folder has no files yet";
        EmptyDetail.Text = _model.HasFilter
            ? $"Nothing here matches “{_model.FilterText}”."
            : "Create a folder, drop files here, or use the button below\nto start building your object storage.";
        EmptyUploadButton.IsVisible = !_model.HasFilter;

        SelectAllButton.Content = _model.AllSelected ? "Unselect all" : "Select all";
        SelectAllButton.IsEnabled = hasItems;
        ClearFilter.IsVisible = _model.FilterText.Length > 0;

        if (!_syncingFilter && FilterBox.Text != _model.FilterText)
        {
            _syncingFilter = true;
            FilterBox.Text = _model.FilterText;
            _syncingFilter = false;
        }

        SyncSelectionUi();
        SyncStatusBar();
    }

    private void SyncStatusBar()
    {
        CountsLabel.Text = _model.CountsText;
        LoadMoreButton.IsVisible = _model.IsTruncated;
        BusyBar.IsVisible = _model.IsBusy;
        LastSyncLabel.Text = _model.LastSyncedText;

        var active = _model.Transfers.ActiveCount;
        ActiveTransfers.IsVisible = active > 0;
        ActiveTransfersLabel.Text = $"{active} transfer{(active == 1 ? "" : "s")} active";
    }

    private void SyncSelectionUi()
    {
        var selected = _model.SelectedObjects;
        SelectionBar.IsVisible = selected.Count > 0;
        SelectionCount.Text = $"{selected.Count} selected";

        var single = selected.Count == 1;
        var allFolders = selected.Count > 0 && selected.All(item => item.IsFolder);
        MoveButton.IsEnabled = !allFolders;
        RenameLabel.Text = single ? "Rename…" : "Batch Rename…";
        RenameButton.IsEnabled = single || !allFolders;
        CopyLinkButton.IsVisible = single && !selected[0].IsFolder;
        CopyKeysLabel.Text = single ? "Copy Key" : "Copy Keys";
        SelectAllButton.Content = _model.AllSelected ? "Unselect all" : "Select all";

        if (_model.IsList) SyncListSelection();
    }

    private void SyncListSelection()
    {
        if (_syncingListSelection) return;
        _syncingListSelection = true;
        try
        {
            ListHost.SelectedItems.Clear();
            foreach (var item in _model.Items.Where(candidate => candidate.IsSelected))
            {
                ListHost.SelectedItems.Add(item);
            }
        }
        finally
        {
            _syncingListSelection = false;
        }
    }

    private void RebuildBreadcrumbs()
    {
        _breadcrumbs.Clear();
        var crumbs = _model.Breadcrumbs;
        for (var index = 0; index < crumbs.Count; index++)
        {
            _breadcrumbs.Add(new BreadcrumbItem(
                index == 0 ? "All Files" : crumbs[index].Name,
                crumbs[index].Prefix,
                index > 0,
                index == crumbs.Count - 1 ? FontWeight.SemiBold : FontWeight.Normal));
        }
    }

    /// Fetches image thumbnails for the visible page, a few at a time so a
    /// folder of large photos doesn't saturate the connection pool.
    private void LoadThumbnails()
    {
        var pending = _model.Items.Where(item => item.CanHaveThumbnail && !item.HasThumbnail).ToList();
        if (pending.Count == 0) return;

        _ = Task.Run(async () =>
        {
            foreach (var chunk in pending.Chunk(4))
            {
                var loads = chunk.Select(async item =>
                {
                    var bitmap = await ThumbnailLoader.Shared
                        .ThumbnailAsync(item.Object, item.Bucket, item.Client)
                        .ConfigureAwait(false);
                    if (bitmap != null)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => item.Thumbnail = bitmap);
                    }
                });
                await Task.WhenAll(loads).ConfigureAwait(false);
            }
        });
    }

    // MARK: - Navigation & controls

    private void OnNavigateUp(object? sender, RoutedEventArgs e) => _model.NavigateUp();

    private void OnBreadcrumbClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is BreadcrumbItem crumb) _model.Navigate(crumb.Prefix);
    }

    private void OnFilterChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncingFilter) return;
        _syncingFilter = true;
        _model.FilterText = FilterBox.Text ?? "";
        _syncingFilter = false;
    }

    private void OnClearFilter(object? sender, RoutedEventArgs e) => _model.FilterText = "";

    private void OnViewModeClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button) return;
        _model.ViewMode = (button.Tag as string) == "list" ? ViewMode.List : ViewMode.Grid;
        Sync();
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => _ = _model.RefreshAsync();

    private void OnLoadMore(object? sender, RoutedEventArgs e) => _ = _model.LoadMoreAsync();

    private void OnToggleSelectAll(object? sender, RoutedEventArgs e)
    {
        if (_model.AllSelected) _model.ClearSelection(); else _model.SelectAll();
    }

    private void OnClearSelection(object? sender, RoutedEventArgs e) => _model.ClearSelection();

    private void OnSortMenu(object? sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        foreach (var field in SortFieldExtensions.All)
        {
            var captured = field;
            var item = new MenuItem { Header = field.Label() };
            if (_model.SortField == field) item.Icon = new Glyph { Symbol = "checkmark", Size = 13 };
            item.Click += (_, _) => _model.SortField = captured;
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());

        var ascending = new MenuItem { Header = "Ascending" };
        if (_model.SortAscending) ascending.Icon = new Glyph { Symbol = "checkmark", Size = 13 };
        ascending.Click += (_, _) => _model.SortAscending = true;
        menu.Items.Add(ascending);

        var descending = new MenuItem { Header = "Descending" };
        if (!_model.SortAscending) descending.Icon = new Glyph { Symbol = "checkmark", Size = 13 };
        descending.Click += (_, _) => _model.SortAscending = false;
        menu.Items.Add(descending);

        if (sender is Control control) menu.ShowAt(control);
    }

    private void OnMoreMenu(object? sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        var newFolder = new MenuItem { Header = "New Folder…" };
        newFolder.Click += (_, _) => ShowNewFolder();
        menu.Items.Add(newFolder);
        if (sender is Control control) menu.ShowAt(control);
    }

    private void OnUploadMenu(object? sender, RoutedEventArgs e)
    {
        // Windows has no combined file+folder picker, so the button offers both.
        var menu = new MenuFlyout();

        var files = new MenuItem { Header = "Upload Files…" };
        files.Click += async (_, _) =>
        {
            var paths = await Panels.ChooseFilesForUploadAsync(TopLevel.GetTopLevel(this));
            StartUpload(paths);
        };
        menu.Items.Add(files);

        var folders = new MenuItem { Header = "Upload Folders…" };
        folders.Click += async (_, _) =>
        {
            var paths = await Panels.ChooseFoldersForUploadAsync(TopLevel.GetTopLevel(this));
            StartUpload(paths);
        };
        menu.Items.Add(folders);

        if (sender is Control control) menu.ShowAt(control);
    }

    private void StartUpload(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        _model.Upload(paths);
        ToastCenter.Shared.Show(
            $"Uploading {paths.Count} item{(paths.Count == 1 ? "" : "s")}", style: ToastStyle.Info);
    }

    private void ShowNewFolder()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        _ = NewFolderWindow.ShowAsync(owner, _model);
    }

    // MARK: - Item interaction

    private static ObjectItem? ItemFor(object? sender) => (sender as Control)?.DataContext as ObjectItem;

    private void OnTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (ItemFor(sender) is not { } item) return;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            if (!item.IsSelected) _model.SelectOnly(item);
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;

        var modifiers = e.KeyModifiers;
        if (modifiers.HasFlag(KeyModifiers.Shift) && _selectionAnchor != null)
        {
            _model.SelectRange(_selectionAnchor, item);
        }
        else if (modifiers.HasFlag(KeyModifiers.Control))
        {
            _model.ToggleSelection(item);
            _selectionAnchor = item;
        }
        else
        {
            _model.SelectOnly(item);
            _selectionAnchor = item;
        }
        e.Handled = true;
    }

    private void OnTileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ItemFor(sender) is { } item) Open(item);
    }

    private void OnBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Control source && source.DataContext is ObjectItem) return;
        _model.ClearSelection();
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ListHost.SelectedItem is ObjectItem item) Open(item);
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingListSelection) return;
        _syncingListSelection = true;
        try
        {
            _model.SetSelection(ListHost.SelectedItems
                .OfType<ObjectItem>()
                .Select(item => item.Object)
                .ToList());
        }
        finally
        {
            _syncingListSelection = false;
        }
    }

    /// Header clicks drive the shared sort state instead of the grid's own
    /// sorting, so the list, the grid, and the sort menu never disagree.
    private void OnListSorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;
        var field = e.Column.SortMemberPath switch
        {
            "Size" => SortField.Size,
            "SortDate" => SortField.Date,
            "KindLabel" => SortField.Kind,
            _ => SortField.Name
        };
        if (_model.SortField == field)
        {
            _model.SortAscending = !_model.SortAscending;
        }
        else
        {
            _model.SortField = field;
            _model.SortAscending = true;
        }
    }

    private void Open(ObjectItem item)
    {
        if (item.IsFolder)
        {
            _model.Open(item.Object);
        }
        else if (TopLevel.GetTopLevel(this) is Window owner)
        {
            PreviewWindow.Show(owner, item.Object, _model.Bucket, _model.Client);
        }
    }

    // MARK: - Context / action targets

    /// Right-clicking inside the selection acts on the whole selection;
    /// right-clicking elsewhere acts on that one object.
    private List<RemoteObject> Targets(object? sender)
    {
        var selected = _model.SelectedObjects;
        if (ItemFor(sender) is { } item)
        {
            if (selected.Any(candidate => candidate.Id == item.Id)) return selected;
            return new List<RemoteObject> { item.Object };
        }
        return selected;
    }

    private void OnMenuPreview(object? sender, RoutedEventArgs e)
    {
        var first = Targets(sender).FirstOrDefault(item => !item.IsFolder);
        if (first == null || TopLevel.GetTopLevel(this) is not Window owner) return;
        PreviewWindow.Show(owner, first, _model.Bucket, _model.Client);
    }

    private void OnMenuInfo(object? sender, RoutedEventArgs e)
    {
        var first = Targets(sender).FirstOrDefault(item => !item.IsFolder);
        if (first == null || TopLevel.GetTopLevel(this) is not Window owner) return;
        MetadataWindow.Show(owner, first, _model);
    }

    private void OnMenuCopyLink(object? sender, RoutedEventArgs e)
    {
        var first = Targets(sender).FirstOrDefault(item => !item.IsFolder);
        if (first == null) return;
        _ = _model.CopyPresignedLinkAsync(first);
    }

    private async void OnMenuDownload(object? sender, RoutedEventArgs e)
    {
        var targets = Targets(sender);
        if (targets.Count == 0) return;
        var directory = await Panels.ChooseDownloadDirectoryAsync(TopLevel.GetTopLevel(this));
        if (directory == null) return;
        _model.Download(targets, directory);
        ToastCenter.Shared.Show(
            $"Downloading {targets.Count} item{(targets.Count == 1 ? "" : "s")}",
            style: ToastStyle.Info);
    }

    private void OnMenuMove(object? sender, RoutedEventArgs e)
    {
        var targets = Targets(sender);
        if (targets.All(item => item.IsFolder)) return;
        _model.SetSelection(targets);
        if (TopLevel.GetTopLevel(this) is Window owner) MoveWindow.Show(owner, _model);
    }

    private void OnMenuRename(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var targets = Targets(sender);
        if (targets.Count == 1)
        {
            _ = RenameWindow.ShowAsync(owner, _model, targets[0]);
        }
        else if (targets.Count > 1)
        {
            _model.SetSelection(targets);
            _ = BatchRenameWindow.ShowAsync(owner, _model);
        }
    }

    private void OnMenuCopyKey(object? sender, RoutedEventArgs e)
    {
        var targets = Targets(sender);
        if (targets.Count == 0) return;
        var keys = string.Join(Environment.NewLine, targets.Select(item => item.Key));
        _ = ClipboardService.SetTextAsync(keys);
        ToastCenter.Shared.Show($"Copied {targets.Count} key{(targets.Count == 1 ? "" : "s")}");
    }

    private async void OnMenuDelete(object? sender, RoutedEventArgs e)
    {
        var targets = Targets(sender);
        if (targets.Count == 0 || TopLevel.GetTopLevel(this) is not Window owner) return;

        var message = targets.Count == 1
            ? $"Delete “{targets[0].Name}”? This cannot be undone."
            : $"Delete {targets.Count} items? This cannot be undone.";

        var confirmed = await ConfirmWindow.AskAsync(
            owner, "Delete", message, "Delete", destructive: true);
        if (confirmed) await _model.DeleteAsync(targets);
    }

    private async Task ShowErrorAsync(string message)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        await ConfirmWindow.AlertAsync(owner, "Error", message);
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
        DropOverlay.IsVisible = true;
        DropLabel.Text = $"Drop to upload to {_model.Bucket}/{_model.Prefix}";
        e.DragEffects = DragDropEffects.Copy;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e) => DropOverlay.IsVisible = false;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        var paths = PathsFrom(e);
        if (paths.Count == 0) return;
        StartUpload(paths);
    }
}
