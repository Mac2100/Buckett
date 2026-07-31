using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Buckett.Services;

/// One entry in a native popup menu.
public sealed class TrayMenuItem
{
    public string Title { get; init; } = "";
    public bool IsSeparator { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool IsChecked { get; init; }
    public bool IsDefault { get; init; }
    public Action? Action { get; init; }
    public List<TrayMenuItem>? Submenu { get; init; }

    public static TrayMenuItem Separator() => new() { IsSeparator = true };
}

/// A shell notification-area icon implemented directly on top of
/// Shell_NotifyIcon — the Windows counterpart of macOS's NSStatusItem.
/// Owns a hidden window so it can receive the icon's callback messages, show a
/// native popup menu, and raise balloon notifications (which Windows 10/11
/// surface as toasts in the Action Center).
public sealed class NativeTray : IDisposable
{
    private const int WM_APP = 0x8000;
    private const int CallbackMessage = WM_APP + 1;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int WM_DESTROY = 0x0002;
    private const int WM_NULL = 0x0000;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_STATE = 0x00000008;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIS_HIDDEN = 0x00000001;

    private const uint NIIF_INFO = 0x00000001;
    private const uint NIIF_WARNING = 0x00000002;
    private const uint NIIF_ERROR = 0x00000003;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_POPUP = 0x00000010;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint MF_CHECKED = 0x00000008;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_DEFAULT = 0x00001000;

    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    private readonly WndProcDelegate _wndProc;
    private readonly string _className = "BuckettTrayWindow_" + Guid.NewGuid().ToString("N");
    private IntPtr _hwnd;
    private IntPtr _icon;
    private bool _added;
    private bool _hidden;
    private string _tooltip = "Buckett";
    private readonly Dictionary<uint, Action> _commands = new();
    private uint _nextCommandID = 1;

    /// Raised when the icon is left-clicked (the "open the app" gesture).
    public event Action? Activated;

    /// Supplies the menu shown on right-click, rebuilt every time.
    public Func<List<TrayMenuItem>>? MenuProvider;

    public NativeTray()
    {
        _wndProc = WindowProc;
        CreateWindow();
    }

    public bool IsAvailable => _hwnd != IntPtr.Zero;

    private void CreateWindow()
    {
        try
        {
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandle(null),
                lpszClassName = _className
            };
            if (RegisterClassEx(ref wc) == 0)
            {
                Log.Warn($"tray window class registration failed ({Marshal.GetLastWin32Error()})");
                return;
            }
            _hwnd = CreateWindowEx(
                0, _className, "Buckett", 0x80000000 /* WS_POPUP */,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                Log.Warn($"tray window creation failed ({Marshal.GetLastWin32Error()})");
            }
        }
        catch (Exception error)
        {
            Log.Warn($"tray unavailable: {error.Message}");
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case CallbackMessage:
            {
                var mouseMessage = (int)(lParam.ToInt64() & 0xFFFF);
                switch (mouseMessage)
                {
                    case WM_LBUTTONUP:
                    case WM_LBUTTONDBLCLK:
                        Activated?.Invoke();
                        return IntPtr.Zero;
                    case WM_RBUTTONUP:
                    case WM_CONTEXTMENU:
                        ShowMenu();
                        return IntPtr.Zero;
                }
                return IntPtr.Zero;
            }
            case WM_DESTROY:
                return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    // MARK: - Icon lifecycle

    public void Show(string tooltip)
    {
        if (_hwnd == IntPtr.Zero) return;
        _tooltip = tooltip;
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        if (_added)
        {
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }
        else if (Shell_NotifyIcon(NIM_ADD, ref data))
        {
            _added = true;
            var version = NewData(0);
            version.uVersion = 4; // NOTIFYICON_VERSION_4
            Shell_NotifyIcon(NIM_SETVERSION, ref version);
        }
    }

    /// Hides or reveals the icon without giving up the registration, so
    /// notifications keep working while the icon is out of the way.
    public void SetHidden(bool hidden)
    {
        _hidden = hidden;
        if (!_added || _hwnd == IntPtr.Zero) return;
        var data = NewData(NIF_STATE);
        data.dwState = hidden ? NIS_HIDDEN : 0;
        data.dwStateMask = NIS_HIDDEN;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void SetIcon(IntPtr icon)
    {
        var previous = _icon;
        _icon = icon;
        if (_added)
        {
            var data = NewData(NIF_ICON);
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }
        if (previous != IntPtr.Zero && previous != icon) DestroyIcon(previous);
    }

    public void ShowBalloon(string title, string body, BalloonStyle style = BalloonStyle.Info)
    {
        if (!_added || _hidden) return;
        var data = NewData(NIF_INFO);
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(body, 255);
        data.dwInfoFlags = style switch
        {
            BalloonStyle.Warning => NIIF_WARNING,
            BalloonStyle.Error => NIIF_ERROR,
            _ => NIIF_INFO
        };
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public enum BalloonStyle { Info, Warning, Error }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private NOTIFYICONDATA NewData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = Truncate(_tooltip, 127),
        szInfo = "",
        szInfoTitle = ""
    };

    // MARK: - Popup menu

    private void ShowMenu()
    {
        var items = MenuProvider?.Invoke();
        if (items == null || items.Count == 0) return;

        _commands.Clear();
        _nextCommandID = 1;

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        var owned = new List<IntPtr> { menu };
        try
        {
            Populate(menu, items, owned);

            GetCursorPos(out var point);
            SetForegroundWindow(_hwnd);
            var selected = TrackPopupMenuEx(
                menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, point.X, point.Y, _hwnd, IntPtr.Zero);
            PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (selected != 0 && _commands.TryGetValue((uint)selected, out var action))
            {
                action();
            }
        }
        finally
        {
            foreach (var handle in owned) DestroyMenu(handle);
        }
    }

    private void Populate(IntPtr menu, List<TrayMenuItem> items, List<IntPtr> owned)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                continue;
            }

            if (item.Submenu is { Count: > 0 })
            {
                var submenu = CreatePopupMenu();
                owned.Add(submenu);
                Populate(submenu, item.Submenu, owned);
                AppendMenu(menu, MF_STRING | MF_POPUP, (UIntPtr)(ulong)submenu.ToInt64(), item.Title);
                continue;
            }

            var id = _nextCommandID++;
            if (item.Action != null) _commands[id] = item.Action;

            var flags = MF_STRING;
            if (!item.IsEnabled || item.Action == null) flags |= MF_GRAYED;
            if (item.IsChecked) flags |= MF_CHECKED;
            if (item.IsDefault) flags |= MF_DEFAULT;
            AppendMenu(menu, flags, (UIntPtr)id, item.Title);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData(0);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    // MARK: - Interop

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr itemID, string? item);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(
        IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
