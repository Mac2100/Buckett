using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Buckett.Models;
using Buckett.Services;
using Buckett.ViewModels;

namespace Buckett.Views;

public sealed record HeatCell(IBrush Fill, string Tip);
public sealed record RecentRow(string Day, string Files, string Bytes, double Fraction);
public sealed record LargestRow(string Symbol, string Key, string Size);

/// Per-bucket statistics: storage overview cards, upload-activity heatmap
/// (local history), file-type breakdown, and largest objects.
public partial class StatisticsView : UserControl
{
    private readonly AppState _state = AppState.Shared;
    private readonly string _bucket;

    public StatisticsView(string bucket)
    {
        _bucket = bucket;
        InitializeComponent();

        _state.StatsChanged += Sync;
        UploadHistory.Shared.PropertyChanged += (_, _) => Sync();

        Sync();
        if (Stats == null) _state.Analyze(_bucket);
    }

    private BucketStats? Stats =>
        _state.SelectedAccountID is { } id ? _state.Stats(id, _bucket) : null;

    private bool Analyzing =>
        _state.SelectedAccountID is { } id && _state.IsAnalyzing(id, _bucket);

    private void OnAnalyze(object? sender, RoutedEventArgs e) => _state.Analyze(_bucket);

    private void Sync()
    {
        var stats = Stats;

        AnalyzePrompt.IsVisible = stats == null;
        AnalyzingBlock.IsVisible = Analyzing;
        AnalyzeCta.IsVisible = !Analyzing;
        AnalysisRow.IsVisible = stats != null;
        AnalyzedFooter.IsVisible = stats != null;
        ReanalyzeButton.IsEnabled = !Analyzing;
        ReanalyzeSpinner.IsVisible = Analyzing;

        StorageValue.Text = stats?.FormattedSize ?? "—";
        StorageCaption.Text = stats != null ? $"Total {stats.ObjectCount} files" : "Run analyze";
        ObjectsValue.Text = stats?.ObjectCount.ToString(CultureInfo.CurrentCulture) ?? "—";
        ObjectsCaption.Text = stats != null ? $"{stats.ByExtension.Count} file types" : "Run analyze";
        CostValue.Text = stats != null ? CostString(stats.TotalSize) : "—";

        LastChangeValue.Text = stats?.NewestModified is { } newest
            ? RelativeDescription(newest.ToLocalTime())
            : "—";
        LastChangeCaption.Text = stats?.NewestModified is { } newestDate
            ? newestDate.ToLocalTime().ToString("d MMM yyyy, HH:mm")
            : "Run analyze";

        if (stats != null)
        {
            AnalyzedLabel.Text = $"Analyzed {stats.AnalyzedAt.ToLocalTime():d MMM yyyy, HH:mm}";

            var bars = stats.ByExtension
                .Take(8)
                .Select(item => new ChartBar(item.Ext, item.TotalSize, item.FormattedSize))
                .ToList();
            TypesChart.Bars = bars;
            TypesChart.IsVisible = bars.Count > 0;
            NoTypesLabel.IsVisible = bars.Count == 0;

            var largest = stats.LargestObjects
                .Take(8)
                .Select(item => new LargestRow(item.SymbolName, item.Key, item.FormattedSize))
                .ToList();
            LargestHost.ItemsSource = largest;
            NoObjectsLabel.IsVisible = largest.Count == 0;
        }

        SyncHistory();
    }

    private void SyncHistory()
    {
        var history = UploadHistory.Shared;

        var recent30 = history.RecentDays(30);
        var activeDays = recent30.Count(entry => (entry.Day?.files ?? 0) > 0);
        ActiveDaysLabel.Text = $"Active days: {activeDays}/30";

        HeatmapHost.ItemsSource = history.RecentDays(35)
            .Select(entry => new HeatCell(
                HeatBrush(entry.Day?.bytes ?? 0),
                $"{entry.Date:d MMM yyyy}: {entry.Day?.files ?? 0} files, " +
                ByteFormat.String(entry.Day?.bytes ?? 0)))
            .ToList();

        var week = history.RecentDays(7).AsEnumerable().Reverse().ToList();
        var maxBytes = Math.Max(1, week.Max(entry => entry.Day?.bytes ?? 0));
        RecentHost.ItemsSource = week
            .Select((entry, index) => new RecentRow(
                DayLabel(entry.Date, index),
                $"{entry.Day?.files ?? 0} files",
                ByteFormat.String(entry.Day?.bytes ?? 0),
                (double)(entry.Day?.bytes ?? 0) / maxBytes))
            .ToList();
    }

    private IBrush HeatBrush(long bytes)
    {
        if (bytes <= 0)
        {
            return this.FindResource("FaintFillBrush") as IBrush ?? Brushes.LightGray;
        }
        var megabytes = bytes / 1_048_576.0;
        var intensity = Math.Min(1, 0.25 + Math.Log10(Math.Max(1, megabytes)) / 4);
        return new SolidColorBrush(ThemeStore.Shared.Theme.Secondary, intensity);
    }

    private static string DayLabel(DateTime date, int index) => index switch
    {
        0 => "Today",
        1 => "Yesterday",
        _ => date.ToString("ddd d MMM", CultureInfo.CurrentCulture)
    };

    private static string RelativeDescription(DateTime date)
    {
        var delta = DateTime.Now - date;
        if (delta.TotalMinutes < 1) return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} min ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} hr ago";
        if (delta.TotalDays < 31) return $"{(int)delta.TotalDays} days ago";
        if (delta.TotalDays < 365) return $"{(int)(delta.TotalDays / 30)} months ago";
        return $"{(int)(delta.TotalDays / 365)} years ago";
    }

    /// Storage-only estimate, using each provider's published per-GB rate.
    private string CostString(long bytes)
    {
        var gigabytes = bytes / 1_073_741_824.0;
        double freeGB;
        double ratePerGB;
        switch (_state.SelectedAccount?.Provider)
        {
            case Provider.BackblazeB2:
                freeGB = 10;
                ratePerGB = 0.006;
                break;
            case Provider.AmazonS3:
                freeGB = 0;
                ratePerGB = 0.023;   // S3 Standard, first tier
                break;
            default:
                freeGB = 10;
                ratePerGB = 0.015;   // Cloudflare R2
                break;
        }
        var cost = Math.Max(0, gigabytes - freeGB) * ratePerGB;
        return cost.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
    }
}
