using System;
using System.Threading;

namespace Buckett.Services;

/// macOS gives an app single-instance behaviour for free; Windows does not, so
/// launching Buckett twice used to mean two tray icons and two desktop drop
/// targets fighting over the same account store. The second launch now hands
/// off to the first and exits.
///
/// The mutex does double duty: the installer names it in AppMutex, which is how
/// setup and — more to the point — uninstall notice that Buckett is running and
/// ask the user to close it rather than leaving an orphaned process behind.
internal static class SingleInstance
{
    /// Must match AppMutex in build/installer.iss. Inno Setup checks both
    /// names, so hold both: the session-local one is what a per-user install
    /// sees, and the global one covers setup running from another session.
    private const string LocalName = "Buckett.SingleInstance";
    private const string GlobalName = @"Global\Buckett.SingleInstance";
    private const string RevealName = "Buckett.Reveal";

    // Held for the lifetime of the process — a collected mutex is a released
    // one, and the installer would stop seeing us.
    private static Mutex? _local;
    private static Mutex? _global;
    private static EventWaitHandle? _reveal;

    /// True if this process is the first instance and should carry on starting.
    /// False if another Buckett is already running, in which case it has been
    /// asked to show itself and this process should exit quietly.
    internal static bool TryAcquire()
    {
        try
        {
            _local = new Mutex(initiallyOwned: true, LocalName, out var first);

            if (!first)
            {
                SignalExistingInstance();
                return false;
            }

            // Best effort: a global mutex can be denied by policy, and that is
            // not a reason to refuse to start.
            try
            {
                _global = new Mutex(initiallyOwned: true, GlobalName, out _);
            }
            catch (Exception error)
            {
                Log.Warn($"could not claim the global instance mutex: {error.Message}");
            }

            return true;
        }
        catch (Exception error)
        {
            // Losing single-instance protection is a far smaller problem than
            // refusing to launch, so carry on.
            Log.Warn($"single-instance check failed: {error.Message}");
            return true;
        }
    }

    /// Starts listening for a second launch. <paramref name="reveal"/> is
    /// raised off the UI thread, so it must marshal for itself.
    internal static void ListenForReveal(Action reveal)
    {
        try
        {
            _reveal = new EventWaitHandle(false, EventResetMode.AutoReset, RevealName);
        }
        catch (Exception error)
        {
            Log.Warn($"could not listen for a second launch: {error.Message}");
            return;
        }

        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    _reveal.WaitOne();
                    reveal();
                }
                catch (Exception error)
                {
                    Log.Warn($"reveal listener stopped: {error.Message}");
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "Buckett reveal listener"
        };
        thread.Start();
    }

    private static void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(RevealName, out var handle))
            {
                using (handle) handle.Set();
            }
        }
        catch (Exception error)
        {
            Log.Warn($"could not hand off to the running instance: {error.Message}");
        }
    }
}
