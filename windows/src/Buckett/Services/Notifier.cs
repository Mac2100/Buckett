using System;

namespace Buckett.Services;

/// System notifications for app events, gated by per-event user preferences
/// (Settings → Notifications). Delivered through the notification-area icon,
/// which Windows 10/11 surface as toasts in the Action Center.
public sealed class Notifier
{
    public static Notifier Shared { get; } = new();

    public enum Event
    {
        TransfersComplete,
        TransferFailed
    }

    /// Set by the tray controller once the icon exists.
    public NativeTray? Tray { get; set; }

    private Notifier() { }

    public static bool IsEnabled(Event which) => which switch
    {
        Event.TransfersComplete => Settings.Shared.NotifyTransfersComplete,
        Event.TransferFailed => Settings.Shared.NotifyTransferFailed,
        _ => false
    };

    public void Post(Event which, string title, string body)
    {
        if (!IsEnabled(which)) return;
        var style = which == Event.TransferFailed
            ? NativeTray.BalloonStyle.Warning
            : NativeTray.BalloonStyle.Info;
        try
        {
            Tray?.ShowBalloon(title, body, style);
        }
        catch (Exception error)
        {
            Log.Warn($"notification failed: {error.Message}");
        }
    }
}
