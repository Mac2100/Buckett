using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Buckett.Models;
using Buckett.Services;
using Buckett.ViewModels;

namespace Buckett.Views;

/// One entry in the dashboard list: an account heading, a bucket card, or the
/// "no buckets in this account" placeholder.
public sealed class DashboardEntry
{
    public bool IsHeading { get; init; }
    public bool IsPlaceholder { get; init; }
    public bool IsBucket { get; init; }

    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Symbol { get; init; } = "cloud.fill";

    public Account? Account { get; init; }
    public string BucketName { get; init; } = "";

    public bool HasStats { get; init; }
    public bool IsAnalyzing { get; init; }
    public string SizeText { get; init; } = "";
    public string ObjectsText { get; init; } = "";
    public string LastChangeText { get; init; } = "";
    public string AnalyzedText { get; init; } = "";
    public IReadOnlyList<ChartBar> Bars { get; init; } = Array.Empty<ChartBar>();

    public bool HasLastChange => LastChangeText.Length > 0;
    public bool HasBars => HasStats && Bars.Count > 0;
    public bool ShowNoData => IsBucket && !HasStats;
    public bool CanAnalyze => !IsAnalyzing;
    public string NoDataText => IsAnalyzing ? "Analyzing…" : "No usage data yet.";
}

public partial class DashboardView : UserControl
{
    private readonly AppState _state = AppState.Shared;
    private readonly ObservableCollection<DashboardEntry> _entries = new();

    public DashboardView()
    {
        InitializeComponent();
        Host.ItemsSource = _entries;

        _state.BucketsChanged += Rebuild;
        _state.StatsChanged += Rebuild;
        _state.PropertyChanged += OnStateChanged;
        BucketAliases.Shared.PropertyChanged += (_, _) => Rebuild();

        Rebuild();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.SelectedAccountID)
            or nameof(AppState.ShowAllAccounts)
            or nameof(AppState.BucketsLoading))
        {
            Rebuild();
        }
    }

    private IReadOnlyList<Account> OverviewAccounts =>
        _state.ShowAllAccounts
            ? _state.AccountStore.Accounts.ToList()
            : _state.SelectedAccount is { } account
                ? new List<Account> { account }
                : new List<Account>();

    private void Rebuild()
    {
        var accounts = OverviewAccounts;
        var analyzed = accounts
            .SelectMany(account => _state.BucketList(account.Id)
                .Select(bucket => _state.Stats(account.Id, bucket.Name))
                .Where(stats => stats != null)
                .Select(stats => stats!))
            .ToList();
        var totalBuckets = accounts.Sum(account => _state.BucketList(account.Id).Count);

        HeaderTitle.Text = _state.ShowAllAccounts
            ? "All Accounts"
            : _state.SelectedAccount?.DisplayLabel ?? "Dashboard";
        HeaderSubtitle.Text = _state.ShowAllAccounts
            ? $"{accounts.Count} account{(accounts.Count == 1 ? "" : "s")} · " +
              $"{totalBuckets} bucket{(totalBuckets == 1 ? "" : "s")}"
            : _state.SelectedAccount is { } selected
                ? $"{selected.Provider.DisplayName()} · {totalBuckets} bucket{(totalBuckets == 1 ? "" : "s")}"
                : "";

        SummaryRow.IsVisible = analyzed.Count > 0;
        SummaryStorage.Text = ByteFormat.String(analyzed.Sum(stats => stats.TotalSize));
        SummaryObjects.Text = analyzed
            .Sum(stats => stats.ObjectCount)
            .ToString(CultureInfo.CurrentCulture);
        SummaryAnalyzed.Text = $"{analyzed.Count} of {totalBuckets}";

        _entries.Clear();
        foreach (var account in accounts)
        {
            if (_state.ShowAllAccounts)
            {
                _entries.Add(new DashboardEntry
                {
                    IsHeading = true,
                    Title = account.DisplayLabel,
                    Subtitle = "· " + account.Provider.DisplayName(),
                    Symbol = account.Provider.SymbolName()
                });
            }

            var buckets = _state.BucketList(account.Id);
            if (buckets.Count == 0)
            {
                _entries.Add(new DashboardEntry
                {
                    IsPlaceholder = true,
                    Title = "No buckets in this account."
                });
                continue;
            }

            foreach (var bucket in buckets)
            {
                var stats = _state.Stats(account.Id, bucket.Name);
                _entries.Add(new DashboardEntry
                {
                    IsBucket = true,
                    Account = account,
                    BucketName = bucket.Name,
                    Title = BucketAliases.Shared.DisplayName(account.Id, bucket.Name),
                    HasStats = stats != null,
                    IsAnalyzing = _state.IsAnalyzing(account.Id, bucket.Name),
                    SizeText = stats?.FormattedSize ?? "",
                    ObjectsText = stats?.ObjectCount.ToString(CultureInfo.CurrentCulture) ?? "",
                    LastChangeText = stats?.NewestModified is { } newest
                        ? newest.ToLocalTime().ToString("d MMM yyyy")
                        : "",
                    AnalyzedText = stats != null
                        ? $"Analyzed {stats.AnalyzedAt.ToLocalTime():HH:mm}"
                        : "",
                    Bars = stats?.ByExtension
                        .Take(8)
                        .Select(item => new ChartBar(item.Ext, item.TotalSize, item.FormattedSize))
                        .ToList() ?? (IReadOnlyList<ChartBar>)Array.Empty<ChartBar>()
                });
            }
        }

        NoBucketsLabel.IsVisible = totalBuckets == 0 && !_state.BucketsLoading;
    }

    private static DashboardEntry? EntryFor(object? sender) =>
        (sender as Control)?.DataContext as DashboardEntry;

    private void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (EntryFor(sender) is not { Account: { } account } entry) return;
        if (_state.SelectedAccountID != account.Id) _state.SelectAccount(account.Id);
        _state.SidebarSelection = SidebarSelection.ForBucket(entry.BucketName);
    }

    private void OnAnalyze(object? sender, RoutedEventArgs e)
    {
        if (EntryFor(sender) is not { Account: { } account } entry) return;
        _state.Analyze(account, entry.BucketName);
    }
}
