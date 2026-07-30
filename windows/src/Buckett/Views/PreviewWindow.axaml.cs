using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Buckett.Models;
using Buckett.Services;

namespace Buckett.Views;

/// Downloads an object to a temporary file and previews it: images and text
/// render inline, everything else offers "Open with default app" — the closest
/// Windows equivalent of macOS Quick Look.
public partial class PreviewWindow : Window
{
    /// Objects above this size are not auto-downloaded for preview.
    private const long MaxPreviewBytes = 256L * 1024 * 1024;
    private const long MaxInlineTextBytes = 4L * 1024 * 1024;

    private readonly RemoteObject _object;
    private readonly string _bucket;
    private readonly S3Client _client;
    private string? _localPath;

    public PreviewWindow(RemoteObject remote, string bucket, S3Client client)
    {
        _object = remote;
        _bucket = bucket;
        _client = client;

        InitializeComponent();
        this.KeepOnScreen();

        Title = remote.Name;
        HeaderGlyph.Symbol = remote.SymbolName;
        NameLabel.Text = remote.Name;
        DetailLabel.Text = $"{remote.FormattedSize}  ·  {remote.Key}";
        LoadingLabel.Text = $"Fetching {remote.Name}…";

        _ = FetchAsync();
    }

    public static void Show(Window owner, RemoteObject remote, string bucket, S3Client client)
    {
        var window = new PreviewWindow(remote, bucket, client);
        _ = window.ShowDialog(owner);
    }

    private async Task FetchAsync()
    {
        if (_object.Size > MaxPreviewBytes)
        {
            Fail("This file is too large to preview. Use Download instead.");
            return;
        }

        try
        {
            var directory = Path.Combine(
                Path.GetTempPath(), "BuckettPreviews", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, SafeFileName(_object.Name));

            await _client.DownloadObjectAsync(_bucket, _object.Key, destination);
            _localPath = destination;
            OpenButton.IsVisible = true;
            Present(destination);
        }
        catch (Exception error)
        {
            Fail(error.Message);
        }
    }

    private static string SafeFileName(string name)
    {
        var builder = new StringBuilder(name.Length);
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var character in name)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }
        var result = builder.ToString();
        return result.Length == 0 ? "preview" : result;
    }

    private void Present(string path)
    {
        LoadingBlock.IsVisible = false;
        var extension = _object.FileExtension;

        if (FileKinds.IsPreviewableImage(extension))
        {
            try
            {
                ImageView.Source = new Bitmap(path);
                ImageScroller.IsVisible = true;
                return;
            }
            catch (Exception error)
            {
                Log.Warn($"could not decode {path}: {error.Message}");
            }
        }

        if (FileKinds.IsText(extension) && _object.Size <= MaxInlineTextBytes)
        {
            try
            {
                TextView.Text = File.ReadAllText(path);
                TextScroller.IsVisible = true;
                return;
            }
            catch (Exception error)
            {
                Log.Warn($"could not read {path}: {error.Message}");
            }
        }

        GenericGlyph.Symbol = _object.SymbolName;
        GenericTitle.Text = _object.Name;
        GenericDetail.Text = FileKinds.IsVideo(extension) || FileKinds.IsAudio(extension)
            ? $"{_object.FormattedSize} · media files play in your default Windows player."
            : $"{_object.FormattedSize} · no built-in preview for this file type.";
        GenericBlock.IsVisible = true;
    }

    private void Fail(string message)
    {
        LoadingBlock.IsVisible = false;
        ErrorLabel.Text = message;
        ErrorBlock.IsVisible = true;
    }

    private void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (_localPath != null) ShellHelper.OpenFile(_localPath);
    }

    private void OnDone(object? sender, RoutedEventArgs e) => Close();
}
