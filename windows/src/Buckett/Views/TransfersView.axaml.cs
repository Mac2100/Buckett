using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Buckett.Services;

namespace Buckett.Views;

public enum TransferFilter { All, Queued, Active, Completed, Failed }

public static class TransferFilterExtensions
{
    public static string Label(this TransferFilter filter) => filter switch
    {
        TransferFilter.All => "All",
        TransferFilter.Queued => "Queued",
        TransferFilter.Active => "Active",
        TransferFilter.Completed => "Completed",
        _ => "Failed"
    };

    public static bool Matches(this TransferFilter filter, TransferState state) => filter switch
    {
        TransferFilter.All => true,
        TransferFilter.Queued => state.Kind == TransferStateKind.Queued,
        TransferFilter.Active => state.Kind == TransferStateKind.Running,
        TransferFilter.Completed => state.Kind == TransferStateKind.Completed,
        _ => state.Kind is TransferStateKind.Failed or TransferStateKind.Cancelled
    };

    public static TransferFilter[] All { get; } =
    {
        TransferFilter.All, TransferFilter.Queued, TransferFilter.Active,
        TransferFilter.Completed, TransferFilter.Failed
    };
}

public partial class TransfersView : UserControl
{
    private readonly TransferManager _transfers;
    private readonly ObservableCollection<TransferTask> _filtered = new();
    private TransferFilter _filter = TransferFilter.All;

    public TransfersView(TransferManager transfers)
    {
        _transfers = transfers;
        InitializeComponent();

        Host.ItemsSource = _filtered;
        BuildFilterButtons();

        _transfers.Tasks.CollectionChanged += OnTasksChanged;
        _transfers.PropertyChanged += OnTransfersChanged;

        Refresh();
    }

    private void BuildFilterButtons()
    {
        FilterHost.Children.Clear();
        foreach (var filter in TransferFilterExtensions.All)
        {
            var captured = filter;
            var button = new ToggleButton
            {
                Classes = { "segment" },
                IsChecked = filter == _filter,
                Content = filter == TransferFilter.All && _transfers.Tasks.Count > 0
                    ? $"{filter.Label()}  {_transfers.Tasks.Count}"
                    : filter.Label(),
                Tag = filter
            };
            button.Click += (_, _) =>
            {
                _filter = captured;
                BuildFilterButtons();
                Refresh();
            };
            FilterHost.Children.Add(button);
        }
    }

    private void OnTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var task in e.NewItems?.OfType<TransferTask>() ?? Enumerable.Empty<TransferTask>())
        {
            task.PropertyChanged += OnTaskChanged;
        }
        foreach (var task in e.OldItems?.OfType<TransferTask>() ?? Enumerable.Empty<TransferTask>())
        {
            task.PropertyChanged -= OnTaskChanged;
        }
        Refresh();
        BuildFilterButtons();
    }

    private void OnTaskChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransferTask.State)) Dispatcher.UIThread.Post(Refresh);
    }

    private void OnTransfersChanged(object? sender, PropertyChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => ClearButton.IsEnabled = _transfers.HasFinished);

    private void Refresh()
    {
        _filtered.Clear();
        foreach (var task in _transfers.Tasks.Where(task => _filter.Matches(task.State)))
        {
            _filtered.Add(task);
        }

        var empty = _filtered.Count == 0;
        ListScroller.IsVisible = !empty;
        EmptyBlock.IsVisible = empty;
        EmptyTitle.Text = _filter == TransferFilter.All
            ? "No transfers yet"
            : $"No {_filter.Label().ToLowerInvariant()} transfers";
        ClearButton.IsEnabled = _transfers.HasFinished;
    }

    private static TransferTask? TaskFor(object? sender) =>
        (sender as Control)?.DataContext as TransferTask;

    private void OnClearFinished(object? sender, RoutedEventArgs e)
    {
        _transfers.ClearFinished();
        Refresh();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        if (TaskFor(sender) is { } task) _transfers.Cancel(task);
    }

    private void OnRetry(object? sender, RoutedEventArgs e)
    {
        if (TaskFor(sender) is { } task) _transfers.Retry(task);
    }

    private void OnCopyLink(object? sender, RoutedEventArgs e)
    {
        if (TaskFor(sender) is not { } task) return;
        var url = task.Client.PresignedUrl(task.Bucket, task.Key, TimeSpan.FromDays(7));
        if (url == null) return;
        _ = ClipboardService.SetTextAsync(url);
        ToastCenter.Shared.Show("Share link copied", "Valid for 7 days");
    }

    private void OnReveal(object? sender, RoutedEventArgs e)
    {
        if (TaskFor(sender) is { } task) ShellHelper.RevealInExplorer(task.LocalPath);
    }
}
