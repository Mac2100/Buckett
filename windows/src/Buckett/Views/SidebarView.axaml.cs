using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Buckett.Models;
using Buckett.Services;
using Buckett.Support;
using Buckett.ViewModels;

namespace Buckett.Views;

/// One line in the sidebar list: an account caption, a bucket card, or the
/// "no buckets yet" placeholder.
public sealed class SidebarRow
{
    public bool IsCaption { get; init; }
    public bool IsPlaceholder { get; init; }
    public bool IsBucket { get; init; }

    public string Caption { get; init; } = "";
    public string Symbol { get; init; } = "cloud.fill";
    public string Placeholder { get; init; } = "";

    public Account? Account { get; init; }
    public Bucket? Bucket { get; init; }
    public string DisplayName { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public bool HasStats { get; init; }
    public bool IsSelected { get; init; }
    public bool HasAlias { get; init; }

    public bool HasSubtitle => Subtitle.Length > 0;
    public string AliasMenuTitle => HasAlias ? "Edit Alias…" : "Set Alias…";

    public IBrush IconBrush => IsSelected
        ? Application.Current?.FindResource("ThemePrimaryBrush") as IBrush ?? Brushes.Gray
        : Application.Current?.FindResource("SecondaryTextBrush") as IBrush ?? Brushes.Gray;
}

public partial class SidebarView : UserControl
{
    private readonly AppState _state = AppState.Shared;
    private readonly ObservableCollection<SidebarRow> _rows = new();

    public SidebarView()
    {
        InitializeComponent();

        RowsHost.ItemsSource = _rows;
        VersionLabel.Text = "v" + AppVersion.Current;

        _state.PropertyChanged += OnStateChanged;
        _state.BucketsChanged += Rebuild;
        _state.StatsChanged += Rebuild;
        _state.AccountStore.Accounts.CollectionChanged += (_, _) => Rebuild();
        BucketAliases.Shared.PropertyChanged += (_, _) => Rebuild();

        Rebuild();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.SidebarSelection)
            or nameof(AppState.SelectedAccountID)
            or nameof(AppState.BucketsLoading)
            or nameof(AppState.BucketsError)
            or nameof(AppState.ShowAllAccounts))
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        var hasAccounts = _state.HasAccounts;
        AddFirstAccountButton.IsVisible = !hasAccounts;
        AccountSwitcher.IsVisible = hasAccounts;
        NewBucketButton.IsEnabled = _state.CurrentClient != null;
        BucketsSpinner.IsVisible = _state.BucketsLoading;

        ErrorLabel.IsVisible = _state.HasBucketsError;
        ErrorLabel.Text = _state.BucketsError ?? "";

        SwitcherTitle.Text = SwitcherTitleText();
        SwitcherSubtitle.Text = SwitcherSubtitleText();
        SwitcherIcon.Symbol = _state.ShowAllAccounts
            ? "square.stack.3d.up"
            : _state.SelectedAccount?.Provider.SymbolName() ?? "cloud";

        OverviewRow.Background = _state.SidebarSelection.IsDashboard
            ? Application.Current?.FindResource("ThemePrimarySoftBrush") as IBrush
            : Brushes.Transparent;

        _rows.Clear();
        if (_state.ShowAllAccounts)
        {
            foreach (var account in _state.AccountStore.Accounts)
            {
                _rows.Add(new SidebarRow
                {
                    IsCaption = true,
                    Caption = account.DisplayLabel.ToUpperInvariant(),
                    Symbol = account.Provider.SymbolName()
                });
                AppendBuckets(account);
            }
        }
        else if (_state.SelectedAccount is { } selected)
        {
            AppendBuckets(selected);
        }
    }

    private void AppendBuckets(Account account)
    {
        var buckets = _state.BucketList(account.Id);
        var isSelectedAccount = account.Id == _state.SelectedAccountID;

        if (buckets.Count == 0)
        {
            _rows.Add(new SidebarRow
            {
                IsPlaceholder = true,
                Placeholder = isSelectedAccount && _state.BucketsLoading
                    ? "Loading…"
                    : "No buckets yet. Create one with +"
            });
            return;
        }

        foreach (var bucket in buckets)
        {
            var stats = _state.Stats(account.Id, bucket.Name);
            var subtitle = stats != null
                ? $"{stats.FormattedSize} · {stats.ObjectCount} objects"
                : bucket.CreationDate is { } created
                    ? $"Created {created.ToLocalTime():d MMM yyyy}"
                    : "";

            _rows.Add(new SidebarRow
            {
                IsBucket = true,
                Account = account,
                Bucket = bucket,
                DisplayName = BucketAliases.Shared.DisplayName(account.Id, bucket.Name),
                Subtitle = subtitle,
                HasStats = stats != null,
                HasAlias = BucketAliases.Shared.Alias(account.Id, bucket.Name) != null,
                IsSelected = isSelectedAccount
                             && _state.SidebarSelection.Bucket == bucket.Name
            });
        }
    }

    private string SwitcherTitleText()
    {
        if (_state.ShowAllAccounts) return "All Accounts";
        return _state.SelectedAccount?.DisplayLabel ?? "Select account";
    }

    private string SwitcherSubtitleText()
    {
        if (_state.ShowAllAccounts)
        {
            var count = _state.AccountCount;
            return $"{count} account{(count == 1 ? "" : "s")}";
        }
        return _state.SelectedAccount?.Provider.DisplayName() ?? "";
    }

    // MARK: - Actions

    private void OnAccountSwitcher(object? sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        foreach (var account in _state.AccountStore.Accounts)
        {
            var captured = account;
            var item = new MenuItem
            {
                Header = captured.DisplayLabel,
                Icon = new Glyph { Symbol = captured.Provider.SymbolName(), Size = 14 }
            };
            if (!_state.ShowAllAccounts && captured.Id == _state.SelectedAccountID)
            {
                item.Icon = new Glyph { Symbol = "checkmark", Size = 14 };
            }
            item.Click += (_, _) =>
            {
                _state.ShowAllAccounts = false;
                _state.SelectAccount(captured.Id);
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        var allAccounts = new MenuItem
        {
            Header = "All Accounts",
            Icon = new Glyph
            {
                Symbol = _state.ShowAllAccounts ? "checkmark" : "square.stack.3d.up",
                Size = 14
            }
        };
        allAccounts.Click += (_, _) => _state.ShowAllAccounts = true;
        menu.Items.Add(allAccounts);

        menu.Items.Add(new Separator());
        var add = new MenuItem { Header = "Add Account…" };
        add.Click += (_, _) => ShowOnboarding();
        menu.Items.Add(add);

        menu.ShowAt(AccountSwitcher);
    }

    private void OnMoreMenu(object? sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();

        var updates = new MenuItem { Header = "Check for Updates…" };
        updates.Click += async (_, _) => await _state.Updates.CheckAsync(userInitiated: true);
        menu.Items.Add(updates);

        menu.Items.Add(new Separator());

        var star = new MenuItem { Header = "Star on GitHub…" };
        star.Click += (_, _) => ShellHelper.OpenUrl(SupportLinks.GitHubRepo);
        menu.Items.Add(star);

        var coffee = new MenuItem { Header = "Buy Me a Coffee…" };
        coffee.Click += (_, _) => ShellHelper.OpenUrl(SupportLinks.BuyMeACoffee);
        menu.Items.Add(coffee);

        menu.Items.Add(new Separator());

        var about = new MenuItem { Header = "About Buckett" };
        about.Click += (_, _) => SettingsWindow.Present(TopLevel.GetTopLevel(this) as Window, "About");
        menu.Items.Add(about);

        if (sender is Control control) menu.ShowAt(control);
    }

    private void OnAddAccount(object? sender, RoutedEventArgs e) => ShowOnboarding();

    private void ShowOnboarding()
    {
        if (TopLevel.GetTopLevel(this) is Window owner) OnboardingWindow.Present(owner);
    }

    private void OnOpenSettings(object? sender, RoutedEventArgs e) =>
        SettingsWindow.Present(TopLevel.GetTopLevel(this) as Window);

    private void OnSelectOverview(object? sender, RoutedEventArgs e) =>
        _state.SidebarSelection = SidebarSelection.Dashboard;

    private void OnRefreshBuckets(object? sender, RoutedEventArgs e) => _ = _state.LoadBucketsAsync();

    private void OnNewBucket(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner) NewBucketWindow.Show(owner);
    }

    private void OnBucketRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (RowFor(sender) is not { Account: { } account, Bucket: { } bucket }) return;

        if (_state.SelectedAccountID != account.Id) _state.SelectAccount(account.Id);
        _state.SidebarSelection = SidebarSelection.ForBucket(bucket.Name);
    }

    private static SidebarRow? RowFor(object? sender) =>
        (sender as Control)?.DataContext as SidebarRow;

    private void OnEditAlias(object? sender, RoutedEventArgs e)
    {
        if (RowFor(sender) is not { Account: { } account, Bucket: { } bucket }) return;
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            AliasWindow.Show(owner, account.Id, bucket.Name);
        }
    }

    private void OnRemoveAlias(object? sender, RoutedEventArgs e)
    {
        if (RowFor(sender) is not { Account: { } account, Bucket: { } bucket }) return;
        BucketAliases.Shared.SetAlias(null, account.Id, bucket.Name);
    }

    private void OnDeleteBucket(object? sender, RoutedEventArgs e)
    {
        if (RowFor(sender) is not { Account: { } account, Bucket: { } bucket }) return;
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            DeleteBucketWindow.Show(owner, account, bucket);
        }
    }
}
