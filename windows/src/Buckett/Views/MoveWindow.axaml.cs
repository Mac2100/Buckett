using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Buckett.Models;
using Buckett.Services;
using Buckett.ViewModels;

namespace Buckett.Views;

/// "Move Files": pick a destination account and bucket (any account —
/// cross-account moves relay through this PC), browse to a target folder or
/// type a path, choose a conflict strategy, then move.
public partial class MoveWindow : Window
{
    private sealed record AccountChoice(Account Account)
    {
        public override string ToString() => Account.DisplayLabel;
    }

    private sealed record BucketChoice(string Name, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private readonly AppState _state = AppState.Shared;
    private readonly BrowserModel _model;
    private readonly ObservableCollection<Breadcrumb> _crumbs = new();
    private readonly List<RadioButton> _strategyButtons = new();

    private string _browsePrefix = "";
    private bool _loadingChoices;

    public MoveWindow(BrowserModel model)
    {
        _model = model;
        InitializeComponent();
        this.KeepOnScreen();

        CrumbHost.ItemsSource = _crumbs;
        BuildStrategyOptions();

        AccountPicker.ItemsSource = _state.AccountStore.Accounts
            .Select(account => new AccountChoice(account))
            .ToList();
        AccountPicker.SelectedIndex = Math.Max(0, _state.AccountStore.Accounts
            .ToList()
            .FindIndex(account => account.Id == _state.SelectedAccountID));

        AccountPicker.SelectionChanged += (_, _) =>
        {
            if (_loadingChoices) return;
            ReloadBuckets(preferred: null);
        };
        BucketPicker.SelectionChanged += (_, _) =>
        {
            if (_loadingChoices) return;
            _browsePrefix = "";
            _ = RefreshFoldersAsync();
            SyncSummary();
        };
        CustomPath.TextChanged += (_, _) => SyncSummary();

        ReloadBuckets(preferred: _model.Bucket);
    }

    public static void Show(Window owner, BrowserModel model)
    {
        var window = new MoveWindow(model);
        _ = window.ShowDialog(owner);
    }

    private void BuildStrategyOptions()
    {
        foreach (var strategy in ConflictStrategyExtensions.All)
        {
            var button = new RadioButton
            {
                GroupName = "conflict",
                Content = $"{strategy.Label()} — {strategy.Detail()}",
                Tag = strategy,
                IsChecked = strategy == ConflictStrategy.Skip
            };
            _strategyButtons.Add(button);
            StrategyHost.Children.Add(button);
        }
    }

    private ConflictStrategy SelectedStrategy =>
        _strategyButtons.FirstOrDefault(button => button.IsChecked == true)?.Tag
            is ConflictStrategy strategy
            ? strategy
            : ConflictStrategy.Skip;

    private Account? DestAccount => (AccountPicker.SelectedItem as AccountChoice)?.Account;

    private S3Client? DestClient => DestAccount is { } account ? _state.Client(account) : null;

    private string DestBucket => (BucketPicker.SelectedItem as BucketChoice)?.Name ?? "";

    private List<RemoteObject> MovingFiles =>
        _model.SelectedObjects.Where(item => !item.IsFolder).ToList();

    /// Effective destination prefix ("" = bucket root).
    private string TargetPrefix
    {
        get
        {
            var typed = (CustomPath.Text ?? "").Trim();
            if (typed.Length > 0)
            {
                var normalized = typed.TrimStart('/');
                if (normalized.Length > 0 && !normalized.EndsWith("/")) normalized += "/";
                return normalized;
            }
            return _browsePrefix;
        }
    }

    private void ReloadBuckets(string? preferred)
    {
        _loadingChoices = true;
        try
        {
            var account = DestAccount;
            var buckets = account == null
                ? new List<BucketChoice>()
                : _state.BucketList(account.Id)
                    .Select(bucket => new BucketChoice(
                        bucket.Name,
                        BucketAliases.Shared.DisplayName(account.Id, bucket.Name)))
                    .ToList();

            BucketPicker.ItemsSource = buckets;
            var index = preferred == null
                ? 0
                : Math.Max(0, buckets.FindIndex(bucket => bucket.Name == preferred));
            BucketPicker.SelectedIndex = buckets.Count > 0 ? index : -1;
            _browsePrefix = "";
        }
        finally
        {
            _loadingChoices = false;
        }

        _ = RefreshFoldersAsync();
        SyncSummary();
    }

    private void SyncSummary()
    {
        var count = MovingFiles.Count;
        var bucketLabel = DestBucket.Length == 0 ? "…" : DestBucket;
        var target = TargetPrefix.Length == 0
            ? $"root of {bucketLabel}"
            : $"{bucketLabel}/{TargetPrefix}";

        SummaryLabel.Text = $"Moving {count} file{(count == 1 ? "" : "s")} to: {target}";
        MoveLabel.Text = $"Move {count} object{(count == 1 ? "" : "s")}";
        MoveButton.IsEnabled = count > 0 && DestClient != null && DestBucket.Length > 0;

        var crossAccount = DestClient != null && !ReferenceEquals(DestClient, _model.Client);
        CrossAccountNote.IsVisible = crossAccount;

        _crumbs.Clear();
        var running = "";
        foreach (var component in _browsePrefix.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            running += component + "/";
            _crumbs.Add(new Breadcrumb(component, running));
        }
    }

    private async Task RefreshFoldersAsync()
    {
        var client = DestClient;
        var bucket = DestBucket;
        if (client == null || bucket.Length == 0)
        {
            FolderHost.ItemsSource = Array.Empty<RemoteObject>();
            NoFoldersLabel.IsVisible = true;
            return;
        }

        FolderSpinner.IsVisible = true;
        try
        {
            var result = await client.ListObjectsAsync(bucket, _browsePrefix);
            FolderHost.ItemsSource = result.Folders;
            NoFoldersLabel.IsVisible = result.Folders.Count == 0;
        }
        catch (Exception error)
        {
            Log.Warn($"could not list folders in {bucket}: {error.Message}");
            FolderHost.ItemsSource = Array.Empty<RemoteObject>();
            NoFoldersLabel.IsVisible = true;
        }
        finally
        {
            FolderSpinner.IsVisible = false;
        }
    }

    private void OnBrowseRoot(object? sender, RoutedEventArgs e)
    {
        _browsePrefix = "";
        SyncSummary();
        _ = RefreshFoldersAsync();
    }

    private void OnCrumbClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not Breadcrumb crumb) return;
        _browsePrefix = crumb.Prefix;
        SyncSummary();
        _ = RefreshFoldersAsync();
    }

    private void OnFolderClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not RemoteObject folder) return;
        _browsePrefix = folder.Key;
        SyncSummary();
        _ = RefreshFoldersAsync();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnMove(object? sender, RoutedEventArgs e)
    {
        var client = DestClient;
        var account = DestAccount;
        if (client == null || account == null || DestBucket.Length == 0) return;

        var files = MovingFiles;
        var target = TargetPrefix;
        var strategy = SelectedStrategy;
        var bucket = DestBucket;
        var display = BucketAliases.Shared.DisplayName(account.Id, bucket);

        Close();
        _ = _model.MoveAsync(files, client, bucket, target, strategy, display);
    }
}
