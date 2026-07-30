using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Buckett.Models;
using Buckett.Services;
using Buckett.ViewModels;

namespace Buckett.Views;

public sealed record ProviderCard(
    string Name, string Symbol, Provider Provider,
    IBrush Background, IBrush Border, IBrush IconBrush);

public sealed record StepDot(
    string Title, string Symbol, IBrush Fill, IBrush IconBrush, IBrush TextBrush,
    bool ShowLeftLine, bool ShowRightLine, IBrush LeftLine, IBrush RightLine);

/// Guided account-setup wizard: Provider → Connection → Credentials → Finish.
public partial class OnboardingWindow : Window
{
    private enum Step { Provider, Connection, Credentials, Finish }

    private static readonly (Step Step, string Title, string Symbol)[] Steps =
    {
        (Step.Provider, "Provider", "cloud"),
        (Step.Connection, "Connection", "link"),
        (Step.Credentials, "Credentials", "key"),
        (Step.Finish, "Finish", "checkmark.seal")
    };

    private readonly AppState _state = AppState.Shared;
    private Step _step = Step.Provider;
    private Provider _provider = Provider.CloudflareR2;

    public OnboardingWindow()
    {
        InitializeComponent();

        NameBox.TextChanged += (_, _) => Sync();
        RegionBox.TextChanged += (_, _) => Sync();
        EndpointBox.TextChanged += (_, _) => Sync();
        AccessKeyBox.TextChanged += (_, _) => Sync();
        SecretKeyBox.TextChanged += (_, _) => Sync();

        Sync();
    }

    public static void Present(Window owner)
    {
        var window = new OnboardingWindow();
        _ = window.ShowDialog(owner);
    }

    private Account Draft()
    {
        var region = (RegionBox.Text ?? "").Trim();
        return new Account
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text)
                ? _provider.DisplayName()
                : NameBox.Text!,
            Provider = _provider,
            CloudflareAccountID = _provider == Provider.CloudflareR2 ? region : "",
            B2Region = _provider == Provider.CloudflareR2 ? "" : region,
            CustomEndpoint = (EndpointBox.Text ?? "").Trim(),
            AccessKeyID = (AccessKeyBox.Text ?? "").Trim()
        };
    }

    private bool CanAdvance() => _step switch
    {
        Step.Provider => true,
        Step.Connection => Draft().EndpointUrl != null,
        Step.Credentials => Draft().AccessKeyID.Length > 0 && (SecretKeyBox.Text ?? "").Length > 0,
        _ => true
    };

    private void Sync()
    {
        ProviderStep.IsVisible = _step == Step.Provider;
        ConnectionStep.IsVisible = _step == Step.Connection;
        CredentialsStep.IsVisible = _step == Step.Credentials;
        FinishStep.IsVisible = _step == Step.Finish;

        PreviousButton.IsVisible = _step != Step.Provider;
        NextButton.Content = _step == Step.Finish ? "Add Account" : "Next";

        var draft = Draft();
        NextButton.IsEnabled = _step == Step.Finish
            ? draft.IsConfigured && (SecretKeyBox.Text ?? "").Length > 0
            : CanAdvance();

        NameBox.Watermark = _provider.DisplayName();

        switch (_provider)
        {
            case Provider.CloudflareR2:
                ConnectionTitle.Text = "Cloudflare account ID";
                ConnectionSubtitle.Text =
                    "Shown on the R2 overview page and in your dashboard URL.";
                RegionLabel.Text = "Account ID";
                RegionBox.Watermark = "32-character hex ID";
                CredentialsHint.Text =
                    "Create an R2 API token with Admin Read & Write so Buckett can list your buckets.";
                break;
            case Provider.BackblazeB2:
                ConnectionTitle.Text = "Backblaze region";
                ConnectionSubtitle.Text = "The part after “s3.” in your bucket's S3 endpoint.";
                RegionLabel.Text = "Region";
                RegionBox.Watermark = "e.g. us-west-004";
                CredentialsHint.Text =
                    "Create an App Key in the Backblaze console; the keyID is your Access Key ID.";
                break;
            default:
                ConnectionTitle.Text = "AWS region";
                ConnectionSubtitle.Text = "The region your buckets live in.";
                RegionLabel.Text = "Region";
                RegionBox.Watermark = "e.g. us-east-1";
                CredentialsHint.Text =
                    "Create an IAM access key with S3 permissions in the AWS console.";
                break;
        }

        EndpointPreview.Text = draft.EndpointUrl?.ToString() ?? "Endpoint not configured yet";
        ConsoleLinkLabel.Text = $"Open {_provider.ShortName()} console to create a key";
        PrivacyNote.Text =
            "Stored only in Windows Credential Manager, used solely to sign requests sent " +
            $"directly to {_provider.DisplayName()}.";
        FinishName.Text = draft.Name;

        BuildProviderCards();
        BuildStepDots();
    }

    private void BuildProviderCards()
    {
        var theme = ThemeStore.Shared.Theme;
        var plainBackground = this.FindResource("FaintFillBrush") as IBrush ?? Brushes.Transparent;
        var plainBorder = this.FindResource("BorderSoftBrush") as IBrush ?? Brushes.Gray;
        var secondary = this.FindResource("SecondaryTextBrush") as IBrush ?? Brushes.Gray;

        ProviderCards.ItemsSource = ProviderExtensions.All
            .Select(provider => new ProviderCard(
                provider.DisplayName(),
                provider.SymbolName(),
                provider,
                provider == _provider
                    ? new SolidColorBrush(theme.Primary, 0.10)
                    : plainBackground,
                provider == _provider
                    ? new SolidColorBrush(theme.Primary, 0.7)
                    : plainBorder,
                provider == _provider ? theme.Gradient : secondary))
            .ToList();
    }

    private void BuildStepDots()
    {
        var theme = ThemeStore.Shared.Theme;
        var idle = this.FindResource("QuaternaryFillBrush") as IBrush ?? Brushes.Gray;
        var primaryText = this.FindResource("PrimaryTextBrush") as IBrush ?? Brushes.Black;
        var secondaryText = this.FindResource("SecondaryTextBrush") as IBrush ?? Brushes.Gray;

        StepHost.ItemsSource = Steps
            .Select((entry, index) =>
            {
                var reached = index <= (int)_step;
                var completed = index < (int)_step;
                return new StepDot(
                    entry.Title,
                    completed ? "checkmark" : entry.Symbol,
                    reached ? theme.Gradient : idle,
                    reached ? Brushes.White : secondaryText,
                    entry.Step == _step ? primaryText : secondaryText,
                    index > 0,
                    index < Steps.Length - 1,
                    reached ? theme.Gradient : idle,
                    index < (int)_step ? theme.Gradient : idle);
            })
            .ToList();
    }

    private void OnPickProvider(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ProviderCard card) return;
        _provider = card.Provider;
        Sync();
    }

    private void OnPrevious(object? sender, RoutedEventArgs e)
    {
        if (_step == Step.Provider) return;
        _step = (Step)((int)_step - 1);
        Sync();
    }

    private void OnNext(object? sender, RoutedEventArgs e)
    {
        if (_step != Step.Finish)
        {
            _step = (Step)((int)_step + 1);
            Sync();
            return;
        }
        Save();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnOpenConsole(object? sender, RoutedEventArgs e) =>
        ShellHelper.OpenUrl(_provider.ConsoleUrl());

    private async void OnTestConnection(object? sender, RoutedEventArgs e)
    {
        var draft = Draft();
        var client = S3Client.Create(draft, SecretKeyBox.Text ?? "");
        if (client == null)
        {
            ShowTestResult(false, "Endpoint is not configured");
            return;
        }

        TestButton.IsEnabled = false;
        TestSpinner.IsVisible = true;
        TestResultBlock.IsVisible = false;
        try
        {
            var buckets = await client.ListBucketsAsync();
            ShowTestResult(
                true, $"Connected — {buckets.Count} bucket{(buckets.Count == 1 ? "" : "s")} visible");
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
        TestResultBlock.IsVisible = true;
        TestGlyph.Symbol = success ? "checkmark.circle.fill" : "xmark.circle.fill";
        var brush = this.FindResource(success ? "SuccessBrush" : "DangerBrush") as IBrush;
        TestGlyph.Foreground = brush;
        TestLabel.Foreground = brush;
        TestLabel.Text = message;

        var showTip = !success
                      && message.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase)
                      && _provider == Provider.CloudflareR2;
        TestTip.IsVisible = showTip;
        TestTip.Text = showTip
            ? "Tip: listing buckets requires an R2 token with Admin Read & Write permission."
            : "";
    }

    private void Save()
    {
        var account = Draft();
        _state.SaveAccount(account, SecretKeyBox.Text);
        _state.SelectAccount(account.Id);
        Close();
        ToastCenter.Shared.Show("Account added", account.Name);
    }
}
