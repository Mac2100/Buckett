using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Buckett.Models;
using Buckett.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Buckett.ViewModels;

/// A bucket qualified by the account it lives in — sidebar sheets need both.
public sealed record AccountBucket(Account Account, Bucket Bucket)
{
    public string Id => Account.Id + "|" + Bucket.Name;
}

public sealed class SidebarSelection : IEquatable<SidebarSelection>
{
    private SidebarSelection(string? bucket) => Bucket = bucket;

    public static readonly SidebarSelection Dashboard = new(null);
    public static SidebarSelection ForBucket(string name) => new(name);

    public string? Bucket { get; }
    public bool IsDashboard => Bucket == null;

    public bool Equals(SidebarSelection? other) => other != null && other.Bucket == Bucket;
    public override bool Equals(object? obj) => Equals(obj as SidebarSelection);
    public override int GetHashCode() => Bucket?.GetHashCode() ?? 0;
}

public sealed class AppState : ObservableObject
{
    public static AppState Shared { get; } = new();

    public AccountStore AccountStore { get; } = new();
    public TransferManager Transfers { get; } = new();
    public UpdateChecker Updates { get; } = new();

    /// Bucket lists for every account (not just the selected one), so the drop
    /// target's menu can offer buckets across accounts.
    private readonly Dictionary<Guid, List<Bucket>> _accountBuckets = new();

    /// Bucket analytics keyed by "accountUUID|bucket" so every account's
    /// buckets can carry stats simultaneously.
    private readonly Dictionary<string, BucketStats> _stats = new();
    private readonly HashSet<string> _analyzing = new();
    private readonly Dictionary<Guid, S3Client> _clientCache = new();
    private readonly Dictionary<string, CancellationTokenSource> _statsRefreshTasks = new();

    private Guid? _selectedAccountID;
    private SidebarSelection _sidebarSelection = SidebarSelection.Dashboard;
    private bool _bucketsLoading;
    private string? _bucketsError;

    /// Raised when bucket lists or stats change, so views can refresh derived lists.
    public event Action? BucketsChanged;
    public event Action? StatsChanged;

    private AppState()
    {
        Transfers.TransferCompleted += bucket => ScheduleStatsRefresh(bucket);

        AccountStore.Accounts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAccounts));
            OnPropertyChanged(nameof(AccountCount));
            BucketsChanged?.Invoke();
        };

        var saved = Settings.Shared.SelectedAccountID;
        if (saved != null && Guid.TryParse(saved, out var id) &&
            AccountStore.Accounts.Any(account => account.Id == id))
        {
            _selectedAccountID = id;
        }
        else
        {
            _selectedAccountID = AccountStore.Accounts.FirstOrDefault()?.Id;
        }
    }

    // MARK: - Selection

    public Guid? SelectedAccountID
    {
        get => _selectedAccountID;
        private set
        {
            if (!SetProperty(ref _selectedAccountID, value)) return;
            Settings.Shared.SelectedAccountID = value?.ToString("D");
            OnPropertyChanged(nameof(SelectedAccount));
        }
    }

    public Account? SelectedAccount =>
        AccountStore.Accounts.FirstOrDefault(account => account.Id == SelectedAccountID);

    public SidebarSelection SidebarSelection
    {
        get => _sidebarSelection;
        set
        {
            if (_sidebarSelection.Equals(value)) return;
            _sidebarSelection = value;
            OnPropertyChanged();
        }
    }

    public bool ShowAllAccounts
    {
        get => Settings.Shared.SidebarShowAllAccounts;
        set
        {
            if (Settings.Shared.SidebarShowAllAccounts == value) return;
            Settings.Shared.SidebarShowAllAccounts = value;
            OnPropertyChanged();
            BucketsChanged?.Invoke();
        }
    }

    public bool HasAccounts => AccountStore.Accounts.Count > 0;
    public int AccountCount => AccountStore.Accounts.Count;

    public bool BucketsLoading
    {
        get => _bucketsLoading;
        private set => SetProperty(ref _bucketsLoading, value);
    }

    public string? BucketsError
    {
        get => _bucketsError;
        private set
        {
            if (SetProperty(ref _bucketsError, value)) OnPropertyChanged(nameof(HasBucketsError));
        }
    }

    public bool HasBucketsError => !string.IsNullOrEmpty(BucketsError);

    // MARK: - Stats

    public static string StatsKey(Guid accountID, string bucket) =>
        accountID.ToString("D").ToUpperInvariant() + "|" + bucket;

    public BucketStats? Stats(Guid accountID, string bucket) =>
        _stats.TryGetValue(StatsKey(accountID, bucket), out var stats) ? stats : null;

    public bool IsAnalyzing(Guid accountID, string bucket) =>
        _analyzing.Contains(StatsKey(accountID, bucket));

    // MARK: - Clients

    public S3Client? Client(Account account)
    {
        if (_clientCache.TryGetValue(account.Id, out var cached)) return cached;
        var client = AccountStore.Client(account);
        if (client != null) _clientCache[account.Id] = client;
        return client;
    }

    public S3Client? CurrentClient => SelectedAccount is { } account ? Client(account) : null;

    // MARK: - Account lifecycle (invalidates cached clients)

    public void SaveAccount(Account account, string? secretKey)
    {
        AccountStore.Upsert(account, secretKey);
        _clientCache.Remove(account.Id);
        if (SelectedAccountID == null) SelectedAccountID = account.Id;
        if (SelectedAccountID == account.Id) _ = LoadBucketsAsync();
        BucketsChanged?.Invoke();
    }

    public void DeleteAccount(Account account)
    {
        AccountStore.Remove(account);
        _clientCache.Remove(account.Id);
        _accountBuckets.Remove(account.Id);
        if (SelectedAccountID == account.Id)
        {
            SelectedAccountID = AccountStore.Accounts.FirstOrDefault()?.Id;
            _stats.Clear();
            SidebarSelection = SidebarSelection.Dashboard;
            _ = LoadBucketsAsync();
        }
        BucketsChanged?.Invoke();
    }

    public void SelectAccount(Guid? id)
    {
        if (id == SelectedAccountID) return;
        SelectedAccountID = id;
        BucketsError = null;
        SidebarSelection = SidebarSelection.Dashboard;
        BucketsChanged?.Invoke();
        _ = LoadBucketsAsync();
    }

    // MARK: - Buckets

    public IReadOnlyList<Bucket> BucketList(Guid accountID) =>
        _accountBuckets.TryGetValue(accountID, out var list)
            ? list
            : Array.Empty<Bucket>();

    public IReadOnlyList<Bucket> Buckets =>
        SelectedAccountID is { } id ? BucketList(id) : Array.Empty<Bucket>();

    public async Task LoadBucketsAsync()
    {
        var account = SelectedAccount;
        if (account == null)
        {
            BucketsChanged?.Invoke();
            return;
        }

        var client = Client(account);
        if (client == null)
        {
            BucketsError =
                $"No credentials found for “{account.DisplayLabel}”. Add the secret key in Settings.";
            _accountBuckets[account.Id] = new List<Bucket>();
            BucketsChanged?.Invoke();
            return;
        }

        BucketsLoading = true;
        BucketsError = null;
        try
        {
            var buckets = await client.ListBucketsAsync().ConfigureAwait(true);
            _accountBuckets[account.Id] = buckets;
            BucketsChanged?.Invoke();
            AutoAnalyze(account);
        }
        catch (Exception error)
        {
            BucketsError = error.Message;
            _accountBuckets[account.Id] = new List<Bucket>();
            BucketsChanged?.Invoke();
        }
        finally
        {
            BucketsLoading = false;
        }

        LoadOtherAccountBuckets();
    }

    /// Background-refreshes bucket lists for accounts other than the selected
    /// one (drop menu, All Accounts sidebar/overview), then kicks off their
    /// stale-stats analysis.
    private void LoadOtherAccountBuckets()
    {
        foreach (var account in AccountStore.Accounts.Where(a => a.Id != SelectedAccountID).ToList())
        {
            var client = Client(account);
            if (client == null) continue;
            _ = Task.Run(async () =>
            {
                try
                {
                    var list = await client.ListBucketsAsync().ConfigureAwait(true);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _accountBuckets[account.Id] = list;
                        BucketsChanged?.Invoke();
                        AutoAnalyze(account);
                    });
                }
                catch (Exception error)
                {
                    Log.Warn($"could not list buckets for {account.DisplayLabel}: {error.Message}");
                }
            });
        }
    }

    public async Task CreateBucketAsync(string name)
    {
        var client = CurrentClient;
        if (client == null) return;
        await client.CreateBucketAsync(name).ConfigureAwait(true);
        await LoadBucketsAsync().ConfigureAwait(true);
    }

    /// Deletes a bucket, optionally emptying it first. Emptying purges ALL
    /// object versions and delete markers (Backblaze B2 keeps hidden versions
    /// that make a bucket look empty while still blocking deletion) and aborts
    /// unfinished multipart uploads. Cleans up aliases, stats, and drop-target
    /// references afterwards.
    public async Task DeleteBucketAsync(string name, Account account, bool emptyFirst)
    {
        var client = Client(account)
            ?? throw new S3Exception(0, "NoCredentials", "Missing account credentials");

        if (emptyFirst)
        {
            var versions = await client.ListAllObjectVersionsAsync(name).ConfigureAwait(true);
            if (versions.Count > 0)
            {
                await client.DeleteObjectVersionsAsync(name, versions).ConfigureAwait(true);
            }
            try
            {
                var uploads = await client.ListMultipartUploadsAsync(name).ConfigureAwait(true);
                foreach (var upload in uploads)
                {
                    try
                    {
                        await client
                            .AbortMultipartUploadAsync(name, upload.Key, upload.UploadID)
                            .ConfigureAwait(true);
                    }
                    catch (Exception error)
                    {
                        Log.Warn($"could not abort upload {upload.UploadID}: {error.Message}");
                    }
                }
            }
            catch (Exception error)
            {
                Log.Warn($"could not list multipart uploads: {error.Message}");
            }
        }

        await client.DeleteBucketAsync(name).ConfigureAwait(true);

        _stats.Remove(StatsKey(account.Id, name));
        BucketAliases.Shared.SetAlias(null, account.Id, name);

        if (_accountBuckets.TryGetValue(account.Id, out var list))
        {
            list.RemoveAll(bucket => bucket.Name == name);
        }
        BucketsChanged?.Invoke();

        if (account.Id == SelectedAccountID)
        {
            if (SidebarSelection.Bucket == name) SidebarSelection = SidebarSelection.Dashboard;
            await LoadBucketsAsync().ConfigureAwait(true);
        }
    }

    /// Raised when something outside the main window needs it brought forward.
    public event Action? OpenMainWindowRequested;

    public void OpenMainWindow() => OpenMainWindowRequested?.Invoke();

    // MARK: - Analytics

    /// Convenience for the selected account.
    public void Analyze(string bucket)
    {
        if (SelectedAccount is { } account) Analyze(account, bucket);
    }

    public void Analyze(Account account, string bucket) => _ = AnalyzeNowAsync(account, bucket);

    public async Task AnalyzeNowAsync(Account account, string bucket)
    {
        var key = StatsKey(account.Id, bucket);
        var client = Client(account);
        if (client == null || _analyzing.Contains(key)) return;

        _analyzing.Add(key);
        StatsChanged?.Invoke();
        try
        {
            var objects = await client.ListAllObjectsAsync(bucket).ConfigureAwait(true);
            _stats[key] = ComputeStats(bucket, objects);
        }
        catch (Exception error)
        {
            Log.Warn($"analyze failed for {bucket}: {error.Message}");
        }
        finally
        {
            _analyzing.Remove(key);
            StatsChanged?.Invoke();
        }
    }

    /// Analyzes the account's buckets that have no stats yet or whose stats
    /// are older than 15 minutes. Sequential so accounts aren't hammered.
    public void AutoAnalyze(Account account)
    {
        var staleBefore = DateTime.UtcNow.AddMinutes(-15);
        var names = BucketList(account.Id).Select(bucket => bucket.Name).ToList();
        _ = AutoAnalyzeSequentiallyAsync(account, names, staleBefore);
    }

    /// One bucket at a time — a wide account shouldn't fire dozens of full
    /// listings at once.
    private async Task AutoAnalyzeSequentiallyAsync(
        Account account, IReadOnlyList<string> names, DateTime staleBefore)
    {
        foreach (var name in names)
        {
            if (_stats.TryGetValue(StatsKey(account.Id, name), out var existing) &&
                existing.AnalyzedAt > staleBefore)
            {
                continue;
            }
            await AnalyzeNowAsync(account, name).ConfigureAwait(true);
        }
    }

    public void AutoAnalyzeAll()
    {
        if (SelectedAccount is { } account) AutoAnalyze(account);
    }

    private void ScheduleStatsRefresh(string bucket)
    {
        // Uploads only carry the bucket name; resolve the owning account,
        // preferring the selected one when names collide across accounts.
        Account? owner = null;
        if (SelectedAccount is { } selected &&
            BucketList(selected.Id).Any(candidate => candidate.Name == bucket))
        {
            owner = selected;
        }
        else
        {
            owner = AccountStore.Accounts.FirstOrDefault(account =>
                BucketList(account.Id).Any(candidate => candidate.Name == bucket));
        }
        if (owner == null) return;

        if (_statsRefreshTasks.TryGetValue(bucket, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }
        var cancellation = new CancellationTokenSource();
        _statsRefreshTasks[bucket] = cancellation;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(4), cancellation.Token).ConfigureAwait(true);
                await Dispatcher.UIThread
                    .InvokeAsync(async () => await AnalyzeNowAsync(owner, bucket))
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer upload into the same bucket.
            }
        });
    }

    public static BucketStats ComputeStats(string bucket, IReadOnlyList<RemoteObject> objects)
    {
        var files = objects.Where(item => !item.IsFolder).ToList();
        var byExtension = files
            .GroupBy(file => file.FileExtension.Length == 0 ? "(none)" : file.FileExtension)
            .Select(group => new ExtensionStat(
                group.Key, group.Count(), group.Sum(file => file.Size)))
            .OrderByDescending(stat => stat.TotalSize)
            .ToList();

        return new BucketStats
        {
            Bucket = bucket,
            ObjectCount = files.Count,
            TotalSize = files.Sum(file => file.Size),
            ByExtension = byExtension,
            LargestObjects = files.OrderByDescending(file => file.Size).Take(10).ToList(),
            NewestModified = files
                .Where(file => file.LastModified.HasValue)
                .Select(file => file.LastModified!.Value)
                .DefaultIfEmpty()
                .Max() is var newest && newest == default ? null : newest,
            AnalyzedAt = DateTime.UtcNow
        };
    }
}
