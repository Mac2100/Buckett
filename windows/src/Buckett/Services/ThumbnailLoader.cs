using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Buckett.Models;

namespace Buckett.Services;

/// Downloads and caches small image thumbnails for the grid view.
public sealed class ThumbnailLoader
{
    public static ThumbnailLoader Shared { get; } = new();

    /// Images larger than this are not fetched for thumbnails.
    public const long MaxBytes = 15 * 1024 * 1024;
    public const int ThumbnailSide = 256;
    private const int CacheLimit = 500;

    private readonly Dictionary<string, Bitmap> _cache = new();
    private readonly LinkedList<string> _order = new();
    private readonly object _gate = new();

    private ThumbnailLoader() { }

    public async Task<Bitmap?> ThumbnailAsync(RemoteObject remote, string bucket, S3Client client)
    {
        if (!remote.IsImage || remote.Size <= 0 || remote.Size > MaxBytes) return null;
        // SVG and a few exotic formats are not decodable by the imaging stack.
        if (!FileKinds.IsPreviewableImage(remote.FileExtension)) return null;

        var cacheKey = $"{bucket}/{remote.Key}/{remote.ETag ?? ""}";
        lock (_gate)
        {
            if (_cache.TryGetValue(cacheKey, out var cached)) return cached;
        }

        try
        {
            var data = await client.GetObjectDataAsync(bucket, remote.Key).ConfigureAwait(false);
            using var stream = new MemoryStream(data);
            var bitmap = Bitmap.DecodeToWidth(stream, ThumbnailSide, BitmapInterpolationMode.HighQuality);

            lock (_gate)
            {
                if (!_cache.ContainsKey(cacheKey))
                {
                    _cache[cacheKey] = bitmap;
                    _order.AddLast(cacheKey);
                    while (_order.Count > CacheLimit && _order.First is { } oldest)
                    {
                        _order.RemoveFirst();
                        if (_cache.Remove(oldest.Value, out var evicted)) evicted.Dispose();
                    }
                }
            }
            return bitmap;
        }
        catch (Exception error)
        {
            Log.Warn($"thumbnail failed for {remote.Key}: {error.Message}");
            return null;
        }
    }
}
