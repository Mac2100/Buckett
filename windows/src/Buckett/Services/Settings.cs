using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Buckett.Services;

/// Where Buckett keeps its per-user files — the Windows analog of
/// ~/Library/Application Support/Buckett on macOS.
public static class AppPaths
{
    public static string SupportDirectory
    {
        get
        {
            var baseDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create);
            if (string.IsNullOrEmpty(baseDirectory)) baseDirectory = Path.GetTempPath();
            return Path.Combine(baseDirectory, "Buckett");
        }
    }

    public static string ResumableDirectory => Path.Combine(SupportDirectory, "resumable");

    public static string EnsureSupportDirectory()
    {
        var directory = SupportDirectory;
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string TempSubdirectory(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(directory);
        return directory;
    }
}

/// Persisted user preferences — the Windows stand-in for UserDefaults /
/// @AppStorage. Backed by a single JSON file so settings survive reinstalls
/// and can be inspected by hand.
public sealed partial class Settings : ObservableObject
{
    public static Settings Shared { get; } = Load();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string FilePath => Path.Combine(AppPaths.SupportDirectory, "settings.json");

    private bool _loading;

    // MARK: - Stored values

    private string? _selectedAccountID;
    private string _defaultViewMode = "grid";
    private string _sortField = "name";
    private bool _sortAscending = true;
    private int _maxConcurrentTransfers = 3;
    private bool _showTrayIcon = true;
    private bool _showDropTarget = true;
    private double _dropTargetX = -1;
    private double _dropTargetY = -1;
    private bool _openAtLogin;
    private bool _notifyTransfersComplete = true;
    private bool _notifyTransferFailed = true;
    private bool _notifyDropStarted;
    private bool _showToasts = true;
    private bool _autoCheckUpdates = true;
    private string _themeID = "bucket";
    private string _appearanceMode = "system";
    private string _trayIconStyle = "archivebox.fill";
    private List<string> _trayDropBuckets = new();
    private bool _sidebarShowAllAccounts;

    public string? SelectedAccountID
    {
        get => _selectedAccountID;
        set => Store(ref _selectedAccountID, value);
    }

    public string DefaultViewMode
    {
        get => _defaultViewMode;
        set => Store(ref _defaultViewMode, value);
    }

    public string SortField
    {
        get => _sortField;
        set => Store(ref _sortField, value);
    }

    public bool SortAscending
    {
        get => _sortAscending;
        set => Store(ref _sortAscending, value);
    }

    public int MaxConcurrentTransfers
    {
        get => _maxConcurrentTransfers;
        set => Store(ref _maxConcurrentTransfers, Math.Clamp(value, 1, 8));
    }

    public bool ShowTrayIcon
    {
        get => _showTrayIcon;
        set => Store(ref _showTrayIcon, value);
    }

    /// The floating desktop drop pad — the Windows stand-in for the macOS
    /// menu bar drop target (tray icons cannot receive dropped files).
    public bool ShowDropTarget
    {
        get => _showDropTarget;
        set => Store(ref _showDropTarget, value);
    }

    public double DropTargetX
    {
        get => _dropTargetX;
        set => Store(ref _dropTargetX, value);
    }

    public double DropTargetY
    {
        get => _dropTargetY;
        set => Store(ref _dropTargetY, value);
    }

    public bool OpenAtLogin
    {
        get => _openAtLogin;
        set => Store(ref _openAtLogin, value);
    }

    public bool NotifyTransfersComplete
    {
        get => _notifyTransfersComplete;
        set => Store(ref _notifyTransfersComplete, value);
    }

    public bool NotifyTransferFailed
    {
        get => _notifyTransferFailed;
        set => Store(ref _notifyTransferFailed, value);
    }

    public bool NotifyDropStarted
    {
        get => _notifyDropStarted;
        set => Store(ref _notifyDropStarted, value);
    }

    public bool ShowToasts
    {
        get => _showToasts;
        set => Store(ref _showToasts, value);
    }

    public bool AutoCheckUpdates
    {
        get => _autoCheckUpdates;
        set => Store(ref _autoCheckUpdates, value);
    }

    public string ThemeID
    {
        get => _themeID;
        set => Store(ref _themeID, value);
    }

    public string AppearanceMode
    {
        get => _appearanceMode;
        set => Store(ref _appearanceMode, value);
    }

    public string TrayIconStyle
    {
        get => _trayIconStyle;
        set => Store(ref _trayIconStyle, value);
    }

    /// Buckets the user checked for the drop-target hover menu, encoded as
    /// "accountUUID|bucketName".
    public List<string> TrayDropBuckets
    {
        get => _trayDropBuckets;
        set => Store(ref _trayDropBuckets, value ?? new List<string>());
    }

    public bool SidebarShowAllAccounts
    {
        get => _sidebarShowAllAccounts;
        set => Store(ref _sidebarShowAllAccounts, value);
    }

    // MARK: - Plumbing

    private void Store<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
        if (!_loading) Save();
    }

    /// Replaces the shortlist and persists it (List mutations can't be observed).
    public void SetTrayDropBuckets(IEnumerable<string> buckets)
    {
        _trayDropBuckets = new List<string>(buckets);
        OnPropertyChanged(nameof(TrayDropBuckets));
        Save();
    }

    private sealed class Snapshot
    {
        public string? SelectedAccountID { get; set; }
        public string? DefaultViewMode { get; set; }
        public string? SortField { get; set; }
        public bool? SortAscending { get; set; }
        public int? MaxConcurrentTransfers { get; set; }
        public bool? ShowTrayIcon { get; set; }
        public bool? ShowDropTarget { get; set; }
        public double? DropTargetX { get; set; }
        public double? DropTargetY { get; set; }
        public bool? OpenAtLogin { get; set; }
        public bool? NotifyTransfersComplete { get; set; }
        public bool? NotifyTransferFailed { get; set; }
        public bool? NotifyDropStarted { get; set; }
        public bool? ShowToasts { get; set; }
        public bool? AutoCheckUpdates { get; set; }
        public string? ThemeID { get; set; }
        public string? AppearanceMode { get; set; }
        public string? TrayIconStyle { get; set; }
        public List<string>? TrayDropBuckets { get; set; }
        public bool? SidebarShowAllAccounts { get; set; }
    }

    private static Settings Load()
    {
        var settings = new Settings { _loading = true };
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var snapshot = JsonSerializer.Deserialize<Snapshot>(json, SerializerOptions);
                if (snapshot != null) settings.Apply(snapshot);
            }
        }
        catch (Exception error)
        {
            Log.Warn($"failed to read settings: {error.Message}");
        }
        settings._loading = false;
        return settings;
    }

    private void Apply(Snapshot snapshot)
    {
        SelectedAccountID = snapshot.SelectedAccountID;
        if (snapshot.DefaultViewMode != null) DefaultViewMode = snapshot.DefaultViewMode;
        if (snapshot.SortField != null) SortField = snapshot.SortField;
        if (snapshot.SortAscending.HasValue) SortAscending = snapshot.SortAscending.Value;
        if (snapshot.MaxConcurrentTransfers.HasValue) MaxConcurrentTransfers = snapshot.MaxConcurrentTransfers.Value;
        if (snapshot.ShowTrayIcon.HasValue) ShowTrayIcon = snapshot.ShowTrayIcon.Value;
        if (snapshot.ShowDropTarget.HasValue) ShowDropTarget = snapshot.ShowDropTarget.Value;
        if (snapshot.DropTargetX.HasValue) DropTargetX = snapshot.DropTargetX.Value;
        if (snapshot.DropTargetY.HasValue) DropTargetY = snapshot.DropTargetY.Value;
        if (snapshot.OpenAtLogin.HasValue) OpenAtLogin = snapshot.OpenAtLogin.Value;
        if (snapshot.NotifyTransfersComplete.HasValue) NotifyTransfersComplete = snapshot.NotifyTransfersComplete.Value;
        if (snapshot.NotifyTransferFailed.HasValue) NotifyTransferFailed = snapshot.NotifyTransferFailed.Value;
        if (snapshot.NotifyDropStarted.HasValue) NotifyDropStarted = snapshot.NotifyDropStarted.Value;
        if (snapshot.ShowToasts.HasValue) ShowToasts = snapshot.ShowToasts.Value;
        if (snapshot.AutoCheckUpdates.HasValue) AutoCheckUpdates = snapshot.AutoCheckUpdates.Value;
        if (snapshot.ThemeID != null) ThemeID = snapshot.ThemeID;
        if (snapshot.AppearanceMode != null) AppearanceMode = snapshot.AppearanceMode;
        if (snapshot.TrayIconStyle != null) TrayIconStyle = snapshot.TrayIconStyle;
        if (snapshot.TrayDropBuckets != null) TrayDropBuckets = snapshot.TrayDropBuckets;
        if (snapshot.SidebarShowAllAccounts.HasValue) SidebarShowAllAccounts = snapshot.SidebarShowAllAccounts.Value;
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureSupportDirectory();
            var snapshot = new Snapshot
            {
                SelectedAccountID = SelectedAccountID,
                DefaultViewMode = DefaultViewMode,
                SortField = SortField,
                SortAscending = SortAscending,
                MaxConcurrentTransfers = MaxConcurrentTransfers,
                ShowTrayIcon = ShowTrayIcon,
                ShowDropTarget = ShowDropTarget,
                DropTargetX = DropTargetX,
                DropTargetY = DropTargetY,
                OpenAtLogin = OpenAtLogin,
                NotifyTransfersComplete = NotifyTransfersComplete,
                NotifyTransferFailed = NotifyTransferFailed,
                NotifyDropStarted = NotifyDropStarted,
                ShowToasts = ShowToasts,
                AutoCheckUpdates = AutoCheckUpdates,
                ThemeID = ThemeID,
                AppearanceMode = AppearanceMode,
                TrayIconStyle = TrayIconStyle,
                TrayDropBuckets = TrayDropBuckets,
                SidebarShowAllAccounts = SidebarShowAllAccounts
            };
            var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception error)
        {
            Log.Warn($"failed to save settings: {error.Message}");
        }
    }
}

/// Minimal diagnostic logging (the NSLog analog): a rolling file next to the
/// app's other data, plus the debugger output window.
public static class Log
{
    private static readonly object Gate = new();

    public static void Warn(string message) => Write("WARN", message);
    public static void Info(string message) => Write("INFO", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] Buckett: {message}";
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                AppPaths.EnsureSupportDirectory();
                File.AppendAllText(Path.Combine(AppPaths.SupportDirectory, "buckett.log"), line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
