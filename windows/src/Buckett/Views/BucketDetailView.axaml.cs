using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Buckett.Services;
using Buckett.ViewModels;

namespace Buckett.Views;

/// A bucket with the Files / Transfers / Statistics tabs on top.
public partial class BucketDetailView : UserControl
{
    private readonly string _bucket;
    private readonly S3Client _client;
    private BrowserView? _browser;
    private TransfersView? _transfers;
    private StatisticsView? _statistics;

    /// "<accountId>/<bucket>" — lets the host avoid rebuilding an identical view.
    public string Identity { get; }

    public BucketDetailView(string bucket, S3Client client, string identity)
    {
        _bucket = bucket;
        _client = client;
        Identity = identity;

        InitializeComponent();
        ShowTab("files");
    }

    private void OnTabClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button) return;
        ShowTab(button.Tag as string ?? "files");
    }

    private void ShowTab(string tab)
    {
        FilesTab.IsChecked = tab == "files";
        TransfersTab.IsChecked = tab == "transfers";
        StatisticsTab.IsChecked = tab == "statistics";

        TabHost.Content = tab switch
        {
            "transfers" => _transfers ??= new TransfersView(AppState.Shared.Transfers),
            "statistics" => _statistics ??= new StatisticsView(_bucket),
            _ => _browser ??= new BrowserView(_bucket, _client, AppState.Shared.Transfers)
        };
    }
}
