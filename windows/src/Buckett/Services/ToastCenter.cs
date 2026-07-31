using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Buckett.Services;

public enum ToastStyle { Success, Info, Error }

public sealed class Toast
{
    public Guid Id { get; } = Guid.NewGuid();
    public required string Message { get; init; }
    public string? Detail { get; init; }
    public ToastStyle Style { get; init; } = ToastStyle.Success;

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    public string Symbol => Style switch
    {
        ToastStyle.Info => "info.circle.fill",
        ToastStyle.Error => "exclamationmark.triangle.fill",
        _ => "checkmark.circle.fill"
    };

    public string BrushKey => Style switch
    {
        ToastStyle.Info => "InfoBrush",
        ToastStyle.Error => "WarningBrush",
        _ => "SuccessBrush"
    };
}

/// Transient bottom-trailing notifications ("sync completed", "upload finished", …).
public sealed class ToastCenter
{
    public static ToastCenter Shared { get; } = new();

    public ObservableCollection<Toast> Toasts { get; } = new();

    private ToastCenter() { }

    public void Show(string message, string? detail = null, ToastStyle style = ToastStyle.Success)
    {
        if (!Settings.Shared.ShowToasts) return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(message, detail, style));
            return;
        }

        var toast = new Toast { Message = message, Detail = detail, Style = style };
        Toasts.Add(toast);
        while (Toasts.Count > 3) Toasts.RemoveAt(0);

        _ = DismissLater(toast);
    }

    private async Task DismissLater(Toast toast)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(3400)).ConfigureAwait(true);
        Dispatcher.UIThread.Post(() => Toasts.Remove(toast));
    }
}
