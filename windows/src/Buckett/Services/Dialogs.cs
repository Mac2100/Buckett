using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace Buckett.Services;

/// The clipboard is reached through a TopLevel in Avalonia; the main window
/// registers itself here so view models can copy without holding a view.
public static class ClipboardService
{
    private static IClipboard? _clipboard;

    public static void Register(TopLevel topLevel) => _clipboard = topLevel.Clipboard;

    public static async Task SetTextAsync(string text)
    {
        if (_clipboard == null) return;
        try
        {
            await _clipboard.SetTextAsync(text).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            Log.Warn($"clipboard write failed: {error.Message}");
        }
    }
}

/// File and folder pickers — the NSOpenPanel counterparts.
public static class Panels
{
    private static TopLevel? _owner;

    public static void Register(TopLevel topLevel) => _owner = topLevel;

    private static TopLevel? Resolve(TopLevel? preferred) => preferred ?? _owner;

    public static async Task<IReadOnlyList<string>> ChooseFilesForUploadAsync(TopLevel? owner = null)
    {
        var topLevel = Resolve(owner);
        if (topLevel == null) return Array.Empty<string>();

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose files to upload",
            AllowMultiple = true
        }).ConfigureAwait(true);

        return files.Select(file => file.Path.LocalPath).ToList();
    }

    /// Windows has no single picker for "files or folders", so folder uploads
    /// get their own entry point; drag & drop still accepts both at once.
    public static async Task<IReadOnlyList<string>> ChooseFoldersForUploadAsync(TopLevel? owner = null)
    {
        var topLevel = Resolve(owner);
        if (topLevel == null) return Array.Empty<string>();

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose folders to upload",
            AllowMultiple = true
        }).ConfigureAwait(true);

        return folders.Select(folder => folder.Path.LocalPath).ToList();
    }

    public static async Task<string?> ChooseDownloadDirectoryAsync(TopLevel? owner = null)
    {
        var topLevel = Resolve(owner);
        if (topLevel == null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a destination folder",
            AllowMultiple = false
        }).ConfigureAwait(true);

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }
}
