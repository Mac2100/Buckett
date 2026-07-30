using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Buckett.Models;
using Buckett.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Buckett.ViewModels;

public enum ConflictStrategy { Skip, Replace, Rename }

public static class ConflictStrategyExtensions
{
    public static string Label(this ConflictStrategy strategy) => strategy switch
    {
        ConflictStrategy.Skip => "Skip",
        ConflictStrategy.Replace => "Replace",
        _ => "Rename"
    };

    public static string Detail(this ConflictStrategy strategy) => strategy switch
    {
        ConflictStrategy.Skip => "Keep the original; don't move conflicting files",
        ConflictStrategy.Replace => "Overwrite files with the same name at the target",
        _ => "Auto-add a suffix (-1, -2, …) on conflict"
    };

    public static IReadOnlyList<ConflictStrategy> All { get; } =
        new[] { ConflictStrategy.Skip, ConflictStrategy.Replace, ConflictStrategy.Rename };
}

public sealed record Breadcrumb(string Name, string Prefix);

/// State + actions for browsing a single bucket.
public sealed class BrowserModel : ObservableObject, IDisposable
{
    public string Bucket { get; }
    public S3Client Client { get; }
    public TransferManager Transfers { get; }

    private string _prefix = "";
    private bool _isLoading;
    private bool _isBusy;
    private string? _errorMessage;
    private string _filterText = "";
    private ViewMode _viewMode;
    private SortField _sortField;
    private bool _sortAscending;
    private bool _isTruncated;
    private DateTime? _lastSynced;
    private string? _continuationToken;
    private CancellationTokenSourceBox _refreshDebounce = new();

    private readonly List<RemoteObject> _folders = new();
    private readonly List<RemoteObject> _files = new();
    private readonly HashSet<string> _selection = new();

    /// Raised whenever the displayed item list or selection changes.
    public event Action? ItemsChanged;
    public event Action? SelectionChanged;

    public ObservableCollection<ObjectItem> Items { get; } = new();

    public BrowserModel(string bucket, S3Client client, TransferManager transfers)
    {
        Bucket = bucket;
        Client = client;
        Transfers = transfers;

        _viewMode = ViewModeExtensions.FromRawValue(Settings.Shared.DefaultViewMode);
        _sortField = SortFieldExtensions.FromRawValue(Settings.Shared.SortField);
        _sortAscending = Settings.Shared.SortAscending;

        Transfers.TransferCompleted += OnTransferCompleted;
    }

    public void Dispose()
    {
        Transfers.TransferCompleted -= OnTransferCompleted;
        _refreshDebounce.Cancel();
    }

    private void OnTransferCompleted(string bucket)
    {
        if (bucket != Bucket) return;
        ScheduleRefresh();
    }

    // MARK: - Display

    public string Prefix
    {
        get => _prefix;
        private set
        {
            if (!SetProperty(ref _prefix, value)) return;
            ClearSelection();
            OnPropertyChanged(nameof(Breadcrumbs));
            OnPropertyChanged(nameof(CanNavigateUp));
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value)) OnPropertyChanged(nameof(ShowLoadingPlaceholder));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetProperty(ref _filterText, value)) return;
            OnPropertyChanged(nameof(HasFilter));
            RebuildDisplayItems();
        }
    }

    public bool HasFilter => FilterText.Trim().Length > 0;

    public ViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (!SetProperty(ref _viewMode, value)) return;
            // Toggling grid/list IS the preference — remember it everywhere.
            Settings.Shared.DefaultViewMode = value.RawValue();
            OnPropertyChanged(nameof(IsGrid));
            OnPropertyChanged(nameof(IsList));
        }
    }

    public bool IsGrid => ViewMode == ViewMode.Grid;
    public bool IsList => ViewMode == ViewMode.List;

    public SortField SortField
    {
        get => _sortField;
        set
        {
            if (!SetProperty(ref _sortField, value)) return;
            Settings.Shared.SortField = value.RawValue();
            RebuildDisplayItems();
        }
    }

    public bool SortAscending
    {
        get => _sortAscending;
        set
        {
            if (!SetProperty(ref _sortAscending, value)) return;
            Settings.Shared.SortAscending = value;
            RebuildDisplayItems();
        }
    }

    public bool IsTruncated
    {
        get => _isTruncated;
        private set => SetProperty(ref _isTruncated, value);
    }

    public DateTime? LastSynced
    {
        get => _lastSynced;
        private set
        {
            if (SetProperty(ref _lastSynced, value)) OnPropertyChanged(nameof(LastSyncedText));
        }
    }

    public string LastSyncedText => LastSynced is { } date ? $"Last sync {date:T}" : "";

    public int FolderCount => _folders.Count;
    public int FileCount => _files.Count;
    public string CountsText =>
        $"{FolderCount} folder{(FolderCount == 1 ? "" : "s")}, {FileCount} file{(FileCount == 1 ? "" : "s")}";

    public bool ShowLoadingPlaceholder => IsLoading && Items.Count == 0;
    public bool IsEmpty => Items.Count == 0;

    public IReadOnlyCollection<string> Selection => _selection;
    public int SelectionCount => _selection.Count;
    public bool HasSelection => _selection.Count > 0;

    public List<RemoteObject> SelectedObjects =>
        Items.Where(item => item.IsSelected).Select(item => item.Object).ToList();

    public bool AllSelected => Items.Count > 0 && _selection.Count == Items.Count;

    public void SetSelection(IEnumerable<RemoteObject> objects)
    {
        _selection.Clear();
        foreach (var item in objects) _selection.Add(item.Id);
        SyncSelectionFlags();
        NotifySelection();
    }

    public void ToggleSelection(ObjectItem item)
    {
        if (!_selection.Remove(item.Id)) _selection.Add(item.Id);
        SyncSelectionFlags();
        NotifySelection();
    }

    public void SelectOnly(ObjectItem item)
    {
        _selection.Clear();
        _selection.Add(item.Id);
        SyncSelectionFlags();
        NotifySelection();
    }

    /// Ctrl-free range selection between the anchor and the clicked row.
    public void SelectRange(ObjectItem anchor, ObjectItem target)
    {
        var start = Items.IndexOf(anchor);
        var end = Items.IndexOf(target);
        if (start < 0 || end < 0) { SelectOnly(target); return; }
        if (start > end) (start, end) = (end, start);

        _selection.Clear();
        for (var index = start; index <= end; index++) _selection.Add(Items[index].Id);
        SyncSelectionFlags();
        NotifySelection();
    }

    public void SelectAll()
    {
        _selection.Clear();
        foreach (var item in Items) _selection.Add(item.Id);
        SyncSelectionFlags();
        NotifySelection();
    }

    private void SyncSelectionFlags()
    {
        foreach (var item in Items) item.IsSelected = _selection.Contains(item.Id);
    }

    public void ClearSelection()
    {
        if (_selection.Count == 0) return;
        _selection.Clear();
        SyncSelectionFlags();
        NotifySelection();
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(SelectedObjects));
        SelectionChanged?.Invoke();
    }

    private IEnumerable<RemoteObject> Sorted(IEnumerable<RemoteObject> items)
    {
        IOrderedEnumerable<RemoteObject> ordered = SortField switch
        {
            SortField.Size => items.OrderBy(item => item.Size),
            SortField.Date => items.OrderBy(item => item.SortDate),
            SortField.Kind => items
                .OrderBy(item => item.FileExtension, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, NaturalComparer.Instance),
            _ => items.OrderBy(item => item.Name, NaturalComparer.Instance)
        };
        var result = ordered.ToList();
        if (!SortAscending) result.Reverse();
        return result;
    }

    private void RebuildDisplayItems()
    {
        var query = FilterText.Trim();
        IEnumerable<RemoteObject> folders = _folders;
        IEnumerable<RemoteObject> files = _files;
        if (query.Length > 0)
        {
            folders = folders.Where(item =>
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
            files = files.Where(item =>
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        Items.Clear();
        foreach (var item in Sorted(folders)) Items.Add(new ObjectItem(item, Bucket, Client));
        foreach (var item in Sorted(files)) Items.Add(new ObjectItem(item, Bucket, Client));

        // Drop selections that no longer exist in view.
        var visible = Items.Select(item => item.Id).ToHashSet();
        _selection.RemoveWhere(id => !visible.Contains(id));
        SyncSelectionFlags();

        OnPropertyChanged(nameof(FolderCount));
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(CountsText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowLoadingPlaceholder));
        OnPropertyChanged(nameof(AllSelected));
        NotifySelection();
        ItemsChanged?.Invoke();
    }

    public IReadOnlyList<Breadcrumb> Breadcrumbs
    {
        get
        {
            var crumbs = new List<Breadcrumb> { new(Bucket, "") };
            var running = "";
            foreach (var component in Prefix.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                running += component + "/";
                crumbs.Add(new Breadcrumb(component, running));
            }
            return crumbs;
        }
    }

    public bool CanNavigateUp => Prefix.Length > 0;

    // MARK: - Loading

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var result = await Client.ListObjectsAsync(Bucket, Prefix).ConfigureAwait(true);
            _folders.Clear();
            _folders.AddRange(result.Folders);
            _files.Clear();
            _files.AddRange(result.Objects);
            IsTruncated = result.IsTruncated;
            _continuationToken = result.NextContinuationToken;
            LastSynced = DateTime.Now;
            RebuildDisplayItems();
        }
        catch (Exception error)
        {
            ErrorMessage = error.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadMoreAsync()
    {
        if (_continuationToken == null) return;
        try
        {
            var result = await Client
                .ListObjectsAsync(Bucket, Prefix, continuationToken: _continuationToken)
                .ConfigureAwait(true);
            foreach (var folder in result.Folders)
            {
                if (_folders.All(existing => existing.Key != folder.Key)) _folders.Add(folder);
            }
            _files.AddRange(result.Objects);
            IsTruncated = result.IsTruncated;
            _continuationToken = result.NextContinuationToken;
            RebuildDisplayItems();
        }
        catch (Exception error)
        {
            ErrorMessage = error.Message;
        }
    }

    public Task RefreshAsync() => LoadAsync();

    /// Coalesces bursts of transfer-completed notifications into one reload.
    private void ScheduleRefresh()
    {
        _refreshDebounce.Cancel();
        var box = new CancellationTokenSourceBox();
        _refreshDebounce = box;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(700, box.Token).ConfigureAwait(true);
                await Avalonia.Threading.Dispatcher.UIThread
                    .InvokeAsync(async () => await LoadAsync())
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // A newer transfer superseded this refresh.
            }
        });
    }

    // MARK: - Navigation

    public void Open(RemoteObject folder)
    {
        if (!folder.IsFolder) return;
        Prefix = folder.Key;
        _ = LoadAsync();
    }

    public void Navigate(string newPrefix)
    {
        Prefix = newPrefix;
        _ = LoadAsync();
    }

    public void NavigateUp()
    {
        if (Prefix.Length == 0) return;
        var components = Prefix.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (components.Count > 0) components.RemoveAt(components.Count - 1);
        Navigate(components.Count == 0 ? "" : string.Join("/", components) + "/");
    }

    // MARK: - Uploads

    /// Expands directories recursively into (filePath, key suffix relative to
    /// the current prefix), preserving folder structure.
    public static List<(string Path, string Suffix)> ExpandForUpload(IReadOnlyList<string> paths)
    {
        var result = new List<(string, string)>();
        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var baseName = new DirectoryInfo(path.TrimEnd(Path.DirectorySeparatorChar)).Name;
                    foreach (var child in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        var attributes = File.GetAttributes(child);
                        if (attributes.HasFlag(FileAttributes.Hidden)) continue;
                        var relative = Path.GetRelativePath(path, child).Replace('\\', '/');
                        result.Add((child, baseName + "/" + relative));
                    }
                }
                else if (File.Exists(path))
                {
                    result.Add((path, Path.GetFileName(path)));
                }
            }
            catch (Exception error)
            {
                Log.Warn($"could not expand {path}: {error.Message}");
            }
        }
        return result;
    }

    public void Upload(IReadOnlyList<string> paths)
    {
        var currentPrefix = Prefix;
        _ = Task.Run(async () =>
        {
            var expanded = await Task.Run(() => ExpandForUpload(paths)).ConfigureAwait(false);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var (filePath, suffix) in expanded)
                {
                    Transfers.EnqueueUpload(filePath, Bucket, currentPrefix + suffix, Client);
                }
            });
        });
    }

    // MARK: - Downloads

    public void Download(IReadOnlyList<RemoteObject> objects, string directory)
    {
        _ = Task.Run(async () =>
        {
            foreach (var item in objects)
            {
                if (item.IsFolder)
                {
                    try
                    {
                        var children = await Client
                            .ListAllObjectsAsync(Bucket, item.Key)
                            .ConfigureAwait(true);
                        var parentPrefix = Prefix;
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            foreach (var child in children.Where(c => !c.Key.EndsWith("/")))
                            {
                                var relative = child.Key[parentPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
                                var destination = Path.Combine(directory, relative);
                                Transfers.EnqueueDownload(child, Bucket, destination, Client);
                            }
                        });
                    }
                    catch (Exception error)
                    {
                        await Avalonia.Threading.Dispatcher.UIThread
                            .InvokeAsync(() => ErrorMessage = error.Message);
                    }
                }
                else
                {
                    var destination = Path.Combine(directory, item.Name);
                    var current = item;
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                        () => Transfers.EnqueueDownload(current, Bucket, destination, Client));
                }
            }
        });
    }

    // MARK: - Delete

    public async Task DeleteAsync(IReadOnlyList<RemoteObject> objects)
    {
        IsBusy = true;
        try
        {
            var keys = new List<string>();
            foreach (var item in objects)
            {
                if (item.IsFolder)
                {
                    var children = await Client.ListAllObjectsAsync(Bucket, item.Key).ConfigureAwait(true);
                    keys.AddRange(children.Select(child => child.Key));
                    keys.Add(item.Key); // folder marker, if any
                }
                else
                {
                    keys.Add(item.Key);
                }
            }
            var uniqueKeys = keys.Distinct().ToList();
            await Client.DeleteObjectsAsync(Bucket, uniqueKeys).ConfigureAwait(true);
            ClearSelection();
            await LoadAsync().ConfigureAwait(true);
            ToastCenter.Shared.Show(
                $"Deleted {uniqueKeys.Count} item{(uniqueKeys.Count == 1 ? "" : "s")}");
        }
        catch (Exception error)
        {
            ErrorMessage = error.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // MARK: - Move (copy + delete into another prefix)

    /// Moves the selected files (folders are skipped) to any destination —
    /// same bucket, another bucket in the same account (server-side copy), or
    /// a bucket in a different account/provider (relayed through this PC:
    /// download from the source, upload to the destination).
    public async Task MoveAsync(
        IReadOnlyList<RemoteObject> objects,
        S3Client destClient,
        string destBucket,
        string targetPrefix,
        ConflictStrategy strategy,
        string? destDisplayName = null)
    {
        IsBusy = true;
        var sameAccount = ReferenceEquals(destClient, Client);
        var moved = 0;
        var skipped = 0;
        try
        {
            foreach (var item in objects.Where(candidate => !candidate.IsFolder))
            {
                var destKey = targetPrefix + item.Name;
                if (sameAccount && destBucket == Bucket && destKey == item.Key)
                {
                    skipped++;
                    continue;
                }

                if (await destClient.ObjectExistsAsync(destBucket, destKey).ConfigureAwait(true))
                {
                    switch (strategy)
                    {
                        case ConflictStrategy.Skip:
                            skipped++;
                            continue;
                        case ConflictStrategy.Replace:
                            break;
                        case ConflictStrategy.Rename:
                            var baseName = Path.GetFileNameWithoutExtension(item.Name);
                            var extension = Path.GetExtension(item.Name);
                            var attempt = 1;
                            do
                            {
                                var candidate = extension.Length == 0
                                    ? $"{baseName}-{attempt}"
                                    : $"{baseName}-{attempt}{extension}";
                                destKey = targetPrefix + candidate;
                                attempt++;
                            } while (await destClient
                                .ObjectExistsAsync(destBucket, destKey)
                                .ConfigureAwait(true));
                            break;
                    }
                }

                if (sameAccount)
                {
                    await Client
                        .CopyObjectAsync(Bucket, item.Key, destBucket, destKey)
                        .ConfigureAwait(true);
                }
                else
                {
                    var temp = Path.Combine(
                        Path.GetTempPath(), $"buckett-relay-{Guid.NewGuid():N}");
                    await Client.DownloadObjectAsync(Bucket, item.Key, temp).ConfigureAwait(true);
                    try
                    {
                        await destClient
                            .UploadFileAsync(destBucket, destKey, temp, item.ContentType)
                            .ConfigureAwait(true);
                    }
                    finally
                    {
                        try { File.Delete(temp); } catch { /* best effort */ }
                    }
                }
                await Client.DeleteObjectAsync(Bucket, item.Key).ConfigureAwait(true);
                moved++;
            }

            ClearSelection();
            await LoadAsync().ConfigureAwait(true);

            var detailParts = new List<string>();
            if (destDisplayName != null) detailParts.Add($"→ {destDisplayName}");
            if (skipped > 0) detailParts.Add($"{skipped} skipped");
            ToastCenter.Shared.Show(
                $"Moved {moved} file{(moved == 1 ? "" : "s")}",
                detailParts.Count > 0 ? string.Join(" · ", detailParts) : null);
        }
        catch (Exception error)
        {
            ErrorMessage = error.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// Folder prefixes at an arbitrary prefix — used by the Move dialog's folder browser.
    public async Task<List<RemoteObject>> ListFoldersAsync(string browsePrefix)
    {
        try
        {
            var result = await Client.ListObjectsAsync(Bucket, browsePrefix).ConfigureAwait(true);
            return result.Folders;
        }
        catch
        {
            return new List<RemoteObject>();
        }
    }

    /// Copies a time-limited presigned link for the object to the clipboard.
    public async Task CopyPresignedLinkAsync(RemoteObject item, TimeSpan? expires = null)
    {
        var url = Client.PresignedUrl(Bucket, item.Key, expires ?? TimeSpan.FromDays(7));
        if (url == null)
        {
            ErrorMessage = "Could not create a presigned link.";
            return;
        }
        await ClipboardService.SetTextAsync(url).ConfigureAwait(true);
        ToastCenter.Shared.Show("Share link copied", "Valid for 7 days");
    }

    // MARK: - Rename (copy + delete)

    public async Task RenameAsync(RemoteObject item, string newName)
    {
        var trimmed = newName.Trim();
        if (trimmed.Length == 0 || trimmed == item.Name || trimmed.Contains('/')) return;

        IsBusy = true;
        try
        {
            if (item.IsFolder)
            {
                var newPrefix = Prefix + trimmed + "/";
                var children = await Client.ListAllObjectsAsync(Bucket, item.Key).ConfigureAwait(true);
                foreach (var child in children)
                {
                    var suffix = child.Key[item.Key.Length..];
                    await Client
                        .CopyObjectAsync(Bucket, child.Key, newPrefix + suffix)
                        .ConfigureAwait(true);
                }
                await Client
                    .DeleteObjectsAsync(Bucket, children.Select(child => child.Key).ToList())
                    .ConfigureAwait(true);
            }
            else
            {
                var newKey = Prefix + trimmed;
                await Client.CopyObjectAsync(Bucket, item.Key, newKey).ConfigureAwait(true);
                await Client.DeleteObjectAsync(Bucket, item.Key).ConfigureAwait(true);
            }
            await LoadAsync().ConfigureAwait(true);
            ToastCenter.Shared.Show($"Renamed to {trimmed}");
        }
        catch (Exception error)
        {
            ErrorMessage = error.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// Batch rename: find & replace within the names of the selected files.
    public async Task BatchRenameAsync(string find, string replace)
    {
        if (find.Length == 0) return;
        IsBusy = true;
        try
        {
            foreach (var item in SelectedObjects.Where(candidate => !candidate.IsFolder))
            {
                var newName = item.Name.Replace(find, replace);
                if (newName == item.Name || newName.Length == 0 || newName.Contains('/')) continue;
                var newKey = Prefix + newName;
                await Client.CopyObjectAsync(Bucket, item.Key, newKey).ConfigureAwait(true);
                await Client.DeleteObjectAsync(Bucket, item.Key).ConfigureAwait(true);
            }
            ClearSelection();
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception error)
        {
            ErrorMessage = error.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // MARK: - Folders

    public async Task CreateFolderAsync(string name)
    {
        var trimmed = name.Trim().Trim('/');
        if (trimmed.Length == 0) return;
        try
        {
            await Client
                .PutObjectAsync(Bucket, Prefix + trimmed + "/", Array.Empty<byte>())
                .ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            ToastCenter.Shared.Show("Folder created", trimmed);
        }
        catch (Exception error)
        {
            ErrorMessage = error.Message;
        }
    }

    // MARK: - Metadata

    public async Task<ObjectMetadata?> MetadataAsync(RemoteObject item)
    {
        try
        {
            return await Client.HeadObjectAsync(Bucket, item.Key).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            ErrorMessage = error.Message;
            return null;
        }
    }
}

/// Small holder so a pending debounce can be cancelled without juggling
/// nullable CancellationTokenSource fields at every call site.
internal sealed class CancellationTokenSourceBox
{
    private readonly System.Threading.CancellationTokenSource _source = new();

    public System.Threading.CancellationToken Token => _source.Token;

    public void Cancel()
    {
        try { _source.Cancel(); } catch (ObjectDisposedException) { /* already gone */ }
    }
}

/// Orders names the way Finder and Explorer do: digit runs compare numerically
/// so "file2" sorts before "file10".
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x == null) return y == null ? 0 : -1;
        if (y == null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var startI = i;
                var startJ = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;

                var leftDigits = x[startI..i].TrimStart('0');
                var rightDigits = y[startJ..j].TrimStart('0');
                if (leftDigits.Length != rightDigits.Length)
                {
                    return leftDigits.Length - rightDigits.Length;
                }
                var digitOrder = string.CompareOrdinal(leftDigits, rightDigits);
                if (digitOrder != 0) return digitOrder;
            }
            else
            {
                var order = char.ToLowerInvariant(x[i]).CompareTo(char.ToLowerInvariant(y[j]));
                if (order != 0) return order;
                i++;
                j++;
            }
        }
        return (x.Length - i) - (y.Length - j);
    }
}
