using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Buckett.Models;
using Buckett.Services;
using Buckett.Support;
using Buckett.ViewModels;

namespace Buckett.Views;

public sealed record AccountRow(Guid Id, string Title, string Symbol);

public sealed record SwatchRow(
    string Id, string Name, IBrush Swatch, IBrush Background, IBrush Border, bool IsSelected);

public sealed record TrayIconRow(
    string Symbol, string Name, IBrush Background, IBrush Border, bool IsSelected);

public partial class SettingsWindow : Window
{
    private static SettingsWindow? _open;

    private readonly AppState _state = AppState.Shared;
    private readonly ThemeStore _themes = ThemeStore.Shared;
    private readonly Settings _settings = Settings.Shared;

    private Account? _editing;
    private bool _secretChanged;
    private bool _loading;
    private Provider _draftProvider = Provider.CloudflareR2;
    private readonly List<ToggleButton> _providerButtons = new();
    private readonly List<ToggleButton> _appearanceButtons = new();

    /// The five drop-target glyph styles, matching the macOS menu bar options.
    private static readonly (string Symbol, string Name)[] TrayIconStyles =
    {
        ("archivebox.fill", "Archive"),
        ("basket.fill", "Bucket"),
        ("tray.full.fill", "Tray"),
        ("cloud.fill", "Cloud"),
        ("externaldrive.fill", "Drive")
    };

    public SettingsWindow()
    {
        InitializeComponent();

        BuildProviderSegments();
        BuildAppearanceSegments();
        BuildGeneralTab();
        BuildNotificationsTab();
        BuildUpdatesTab();

        AboutVersion.Text = $"Version {AppVersion.Current}";
        VersionLabel.Text = AppVersion.Current;

        AccountList.SelectionChanged += (_, _) => LoadEditor();
        _state.AccountStore.Accounts.CollectionChanged += (_, _) => ReloadAccounts();
        _state.Updates.PropertyChanged += (_, _) => SyncUpdates();
        SelfUpdater.Shared.PropertyChanged += (_, _) => SyncUpdates();

        ReloadAccounts();
        ReloadSwatches();
        SyncUpdates();
    }

    public static void Present(Window? owner, string? tab = null)
    {
        if (_open != null)
        {
            _open.Activate();
            if (tab != null) _open.SelectTab(tab);
            return;
        }

        var window = new SettingsWindow();
        _open = window;
        window.Closed += (_, _) => _open = null;
        if (tab != null) window.SelectTab(tab);

        if (owner != null) ((Window)window).Show(owner); else ((Window)window).Show();
    }

    private void SelectTab(string tag)
    {
        foreach (var item in Tabs.Items.OfType<TabItem>())
        {
            if ((item.Tag as string) == tag)
            {
                Tabs.SelectedItem = item;
                return;
            }
        }
    }

    // MARK: - Accounts

    private void ReloadAccounts()
    {
        var previous = (AccountList.SelectedItem as AccountRow)?.Id ?? _editing?.Id;
        AccountList.ItemsSource = _state.AccountStore.Accounts
            .Select(account => new AccountRow(
                account.Id,
                string.IsNullOrWhiteSpace(account.Name) ? "Untitled" : account.Name,
                account.Provider.SymbolName()))
            .ToList();

        var rows = AccountList.ItemsSource as IReadOnlyList<AccountRow> ?? Array.Empty<AccountRow>();
        AccountList.SelectedItem = rows.FirstOrDefault(row => row.Id == previous) ?? rows.FirstOrDefault();
        LoadEditor();
    }

    private void LoadEditor()
    {
        var id = (AccountList.SelectedItem as AccountRow)?.Id;
        _editing = _state.AccountStore.Accounts.FirstOrDefault(account => account.Id == id);

        EditorScroller.IsVisible = _editing != null;
        NoAccountBlock.IsVisible = _editing == null;
        RemoveAccountButton.IsEnabled = _editing != null;
        if (_editing == null) return;

        _loading = true;
        try
        {
            NameBox.Text = _editing.Name;
            _draftProvider = _editing.Provider;
            SyncProviderSegments();
            RegionBox.Text = _editing.Provider == Provider.CloudflareR2
                ? _editing.CloudflareAccountID
                : _editing.B2Region;
            EndpointBox.Text = _editing.CustomEndpoint;
            AccessKeyBox.Text = _editing.AccessKeyID;
            SecretKeyBox.Text = CredentialStore.Secret(_editing.Id) ?? "";
            _secretChanged = false;
            TestLabel.Text = "";
            TestGlyph.IsVisible = false;
            SyncConnectionFields();
        }
        finally
        {
            _loading = false;
        }
    }

    private void BuildProviderSegments()
    {
        foreach (var provider in ProviderExtensions.All)
        {
            var captured = provider;
            var button = new ToggleButton
            {
                Classes = { "segment" },
                Content = provider.DisplayName(),
                Tag = provider
            };
            button.Click += (_, _) =>
            {
                _draftProvider = captured;
                SyncProviderSegments();
                SyncConnectionFields();
            };
            _providerButtons.Add(button);
            ProviderHost.Children.Add(button);
        }

        NameBox.TextChanged += (_, _) => SyncConnectionFields();
        RegionBox.TextChanged += (_, _) => SyncConnectionFields();
        EndpointBox.TextChanged += (_, _) => SyncConnectionFields();
        AccessKeyBox.TextChanged += (_, _) => SyncConnectionFields();
        SecretKeyBox.TextChanged += (_, _) =>
        {
            if (!_loading) _secretChanged = true;
            SyncConnectionFields();
        };
    }

    private void SyncProviderSegments()
    {
        foreach (var button in _providerButtons)
        {
            button.IsChecked = (Provider?)button.Tag == _draftProvider;
        }
    }

    private void SyncConnectionFields()
    {
        switch (_draftProvider)
        {
            case Provider.CloudflareR2:
                RegionLabel.Text = "Cloudflare Account ID";
                RegionHint.Text = "Found in the Cloudflare dashboard URL or the R2 overview page.";
                break;
            case Provider.BackblazeB2:
                RegionLabel.Text = "Region (e.g. us-west-004)";
                RegionHint.Text = "The region from your bucket's S3 endpoint: s3.<region>.backblazeb2.com";
                break;
            default:
                RegionLabel.Text = "Region (e.g. us-east-1)";
                RegionHint.Text = "The AWS region your buckets live in.";
                break;
        }

        var draft = Draft();
        ResolvedEndpoint.Text = draft?.EndpointUrl?.ToString() ?? "Not configured yet";
        TestButton.IsEnabled = draft is { IsConfigured: true }
                               && (SecretKeyBox.Text ?? "").Length > 0;
    }

    private Account? Draft()
    {
        if (_editing == null) return null;
        var draft = _editing.Clone();
        draft.Name = NameBox.Text ?? "";
        draft.Provider = _draftProvider;
        var region = (RegionBox.Text ?? "").Trim();
        if (_draftProvider == Provider.CloudflareR2)
        {
            draft.CloudflareAccountID = region;
        }
        else
        {
            draft.B2Region = region;
        }
        draft.CustomEndpoint = (EndpointBox.Text ?? "").Trim();
        draft.AccessKeyID = (AccessKeyBox.Text ?? "").Trim();
        return draft;
    }

    private void OnAddAccount(object? sender, RoutedEventArgs e)
    {
        var account = new Account { Name = "New Account" };
        _state.SaveAccount(account, null);
        ReloadAccounts();
        AccountList.SelectedItem = (AccountList.ItemsSource as IReadOnlyList<AccountRow>)?
            .FirstOrDefault(row => row.Id == account.Id);
    }

    private async void OnRemoveAccount(object? sender, RoutedEventArgs e)
    {
        if (_editing is not { } account) return;
        var confirmed = await ConfirmWindow.AskAsync(
            this,
            "Remove Account",
            $"Remove “{account.DisplayLabel}”? Its credentials will be deleted from " +
            "Windows Credential Manager.",
            "Remove Account",
            destructive: true);
        if (!confirmed) return;

        _state.DeleteAccount(account);
        ReloadAccounts();
    }

    private void OnSaveAccount(object? sender, RoutedEventArgs e)
    {
        if (Draft() is not { } draft) return;
        _state.SaveAccount(draft, _secretChanged ? SecretKeyBox.Text : null);
        _secretChanged = false;
        ReloadAccounts();
        ToastCenter.Shared.Show("Account saved", draft.DisplayLabel);
    }

    private async void OnTestConnection(object? sender, RoutedEventArgs e)
    {
        if (Draft() is not { } draft) return;
        var client = S3Client.Create(draft, SecretKeyBox.Text ?? "");
        if (client == null)
        {
            ShowTestResult(false, "Endpoint is not configured");
            return;
        }

        TestButton.IsEnabled = false;
        TestSpinner.IsVisible = true;
        TestGlyph.IsVisible = false;
        TestLabel.Text = "";
        try
        {
            var buckets = await client.ListBucketsAsync();
            ShowTestResult(true, $"Connected — {buckets.Count} bucket{(buckets.Count == 1 ? "" : "s")} visible");
        }
        catch (Exception error)
        {
            ShowTestResult(false, error.Message);
        }
        finally
        {
            TestSpinner.IsVisible = false;
            TestButton.IsEnabled = true;
        }
    }

    private void ShowTestResult(bool success, string message)
    {
        TestGlyph.IsVisible = true;
        TestGlyph.Symbol = success ? "checkmark.circle.fill" : "xmark.circle.fill";
        TestGlyph.Foreground = this.FindResource(success ? "SuccessBrush" : "DangerBrush") as IBrush;
        TestLabel.Text = message;
        TestLabel.Foreground = TestGlyph.Foreground;
    }

    // MARK: - Appearance

    private void BuildAppearanceSegments()
    {
        foreach (var mode in AppearanceModeExtensions.All)
        {
            var captured = mode;
            var button = new ToggleButton
            {
                Classes = { "segment" },
                Content = mode.Label(),
                Tag = mode,
                IsChecked = mode == _themes.Appearance
            };
            button.Click += (_, _) =>
            {
                _themes.Appearance = captured;
                foreach (var candidate in _appearanceButtons)
                {
                    candidate.IsChecked = (AppearanceMode?)candidate.Tag == captured;
                }
                ReloadSwatches();
            };
            _appearanceButtons.Add(button);
            AppearanceHost.Children.Add(button);
        }
    }

    private void ReloadSwatches()
    {
        var selectedBackground = this.FindResource("ThemePrimaryFaintBrush") as IBrush
                                 ?? Brushes.Transparent;
        var plainBackground = this.FindResource("FaintFillBrush") as IBrush ?? Brushes.Transparent;
        var plainBorder = this.FindResource("BorderSoftBrush") as IBrush ?? Brushes.Gray;

        ThemeHost.ItemsSource = Themes.All
            .Select(theme => new SwatchRow(
                theme.Id,
                theme.Name,
                theme.Gradient,
                theme.Id == _themes.ThemeID ? selectedBackground : plainBackground,
                theme.Id == _themes.ThemeID ? theme.PrimaryBrush : plainBorder,
                theme.Id == _themes.ThemeID))
            .ToList();

        TrayIconHost.ItemsSource = TrayIconStyles
            .Select(style => new TrayIconRow(
                style.Symbol,
                style.Name,
                style.Symbol == _settings.TrayIconStyle ? selectedBackground : plainBackground,
                style.Symbol == _settings.TrayIconStyle
                    ? _themes.Theme.PrimaryBrush
                    : plainBorder,
                style.Symbol == _settings.TrayIconStyle))
            .ToList();
    }

    private void OnPickTheme(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SwatchRow row) return;
        _themes.ThemeID = row.Id;
        ReloadSwatches();
    }

    private void OnPickTrayIcon(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not TrayIconRow row) return;
        _settings.TrayIconStyle = row.Symbol;
        ReloadSwatches();
    }

    // MARK: - General

    private void BuildGeneralTab()
    {
        DefaultViewPicker.ItemsSource = ViewModeExtensions.All.Select(mode => mode.Label()).ToList();
        DefaultViewPicker.SelectedIndex =
            ViewModeExtensions.FromRawValue(_settings.DefaultViewMode) == ViewMode.List ? 1 : 0;
        DefaultViewPicker.SelectionChanged += (_, _) =>
            _settings.DefaultViewMode = DefaultViewPicker.SelectedIndex == 1 ? "list" : "grid";

        ConcurrencyStepper.Value = _settings.MaxConcurrentTransfers;
        ConcurrencyStepper.ValueChanged += (_, _) =>
            _settings.MaxConcurrentTransfers = (int)(ConcurrencyStepper.Value ?? 3);

        TrayIconToggle.IsChecked = _settings.ShowTrayIcon;
        TrayIconToggle.IsCheckedChanged += (_, _) =>
            _settings.ShowTrayIcon = TrayIconToggle.IsChecked == true;

        DropTargetToggle.IsChecked = _settings.ShowDropTarget;
        DropTargetToggle.IsCheckedChanged += (_, _) =>
            _settings.ShowDropTarget = DropTargetToggle.IsChecked == true;

        OpenAtLoginToggle.IsChecked = StartupRegistration.IsRegistered();
        OpenAtLoginToggle.IsCheckedChanged += (_, _) =>
        {
            var enabled = OpenAtLoginToggle.IsChecked == true;
            try
            {
                StartupRegistration.SetRegistered(enabled);
                _settings.OpenAtLogin = enabled;
            }
            catch (Exception error)
            {
                ToastCenter.Shared.Show(
                    "Couldn't update login item", error.Message, ToastStyle.Error);
                OpenAtLoginToggle.IsChecked = !enabled;
            }
        };
    }

    // MARK: - Notifications

    private void BuildNotificationsTab()
    {
        NotifyCompleteToggle.IsChecked = _settings.NotifyTransfersComplete;
        NotifyCompleteToggle.IsCheckedChanged += (_, _) =>
            _settings.NotifyTransfersComplete = NotifyCompleteToggle.IsChecked == true;

        NotifyFailedToggle.IsChecked = _settings.NotifyTransferFailed;
        NotifyFailedToggle.IsCheckedChanged += (_, _) =>
            _settings.NotifyTransferFailed = NotifyFailedToggle.IsChecked == true;

        NotifyDropToggle.IsChecked = _settings.NotifyDropStarted;
        NotifyDropToggle.IsCheckedChanged += (_, _) =>
            _settings.NotifyDropStarted = NotifyDropToggle.IsChecked == true;

        ToastsToggle.IsChecked = _settings.ShowToasts;
        ToastsToggle.IsCheckedChanged += (_, _) =>
            _settings.ShowToasts = ToastsToggle.IsChecked == true;
    }

    // MARK: - Updates

    private void BuildUpdatesTab()
    {
        AutoCheckToggle.IsChecked = _settings.AutoCheckUpdates;
        AutoCheckToggle.IsCheckedChanged += (_, _) =>
            _settings.AutoCheckUpdates = AutoCheckToggle.IsChecked == true;
    }

    private void SyncUpdates()
    {
        var updates = _state.Updates;
        var updater = SelfUpdater.Shared;

        CheckSpinner.IsVisible = updates.IsChecking;
        CheckNowButton.IsEnabled = !updates.IsChecking && !updater.IsBusy;

        UpdateStatusLabel.Text = updates.StatusMessage;
        UpdateGlyph.IsVisible = updates.HasStatusMessage;
        UpdateGlyph.Symbol = updates.StatusSymbol;
        var brush = this.FindResource(updates.StatusBrushKey) as IBrush;
        UpdateGlyph.Foreground = brush;
        UpdateStatusLabel.Foreground = brush;

        InstallButton.IsVisible = updates.IsUpdateAvailable;
        InstallButton.IsEnabled = !updater.IsBusy;
        LastCheckedLabel.Text = updates.LastCheckedText;

        UpdaterPhaseLabel.IsVisible = updater.Phase != UpdatePhase.Idle;
        UpdaterPhaseLabel.Text = updater.Phase switch
        {
            UpdatePhase.Downloading => updater.ProgressText,
            UpdatePhase.Installing => "Installing…",
            UpdatePhase.Relaunching => "Relaunching…",
            UpdatePhase.Failed => updater.FailureMessage ?? "Update failed",
            _ => ""
        };
    }

    private async void OnCheckUpdates(object? sender, RoutedEventArgs e) =>
        await _state.Updates.CheckAsync(userInitiated: true);

    private void OnInstallUpdate(object? sender, RoutedEventArgs e)
    {
        if (_state.Updates.Status.Url is { } url) SelfUpdater.Shared.Install(url);
    }

    private void OnOpenReleases(object? sender, RoutedEventArgs e) =>
        ShellHelper.OpenUrl(SupportLinks.ReleasesPage);

    private void OnOpenRepo(object? sender, RoutedEventArgs e) =>
        ShellHelper.OpenUrl(SupportLinks.GitHubRepo);

    private void OnOpenLicense(object? sender, RoutedEventArgs e) =>
        ShellHelper.OpenUrl(SupportLinks.License);

    private void OnBuyCoffee(object? sender, RoutedEventArgs e) =>
        ShellHelper.OpenUrl(SupportLinks.BuyMeACoffee);
}
