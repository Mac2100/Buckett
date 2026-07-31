using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Buckett.Models;

// MARK: - Provider

public enum Provider
{
    CloudflareR2,
    BackblazeB2,
    AmazonS3
}

public static class ProviderExtensions
{
    /// Stored JSON value — identical to the macOS build so account files are interchangeable.
    public static string RawValue(this Provider p) => p switch
    {
        Provider.CloudflareR2 => "r2",
        Provider.BackblazeB2 => "b2",
        Provider.AmazonS3 => "s3",
        _ => "r2"
    };

    public static Provider FromRawValue(string? raw) => raw switch
    {
        "b2" => Provider.BackblazeB2,
        "s3" => Provider.AmazonS3,
        _ => Provider.CloudflareR2
    };

    public static string DisplayName(this Provider p) => p switch
    {
        Provider.CloudflareR2 => "Cloudflare R2",
        Provider.BackblazeB2 => "Backblaze B2",
        Provider.AmazonS3 => "Amazon S3",
        _ => "Cloudflare R2"
    };

    public static string ShortName(this Provider p) => p switch
    {
        Provider.CloudflareR2 => "R2",
        Provider.BackblazeB2 => "B2",
        Provider.AmazonS3 => "S3",
        _ => "R2"
    };

    public static string SymbolName(this Provider p) => p switch
    {
        Provider.CloudflareR2 => "cloud.fill",
        Provider.BackblazeB2 => "flame.fill",
        Provider.AmazonS3 => "shippingbox.fill",
        _ => "cloud.fill"
    };

    public static string ConsoleUrl(this Provider p) => p switch
    {
        Provider.CloudflareR2 => "https://dash.cloudflare.com/?to=/:account/r2",
        Provider.BackblazeB2 => "https://secure.backblaze.com/b2_buckets.htm",
        Provider.AmazonS3 => "https://console.aws.amazon.com/iam/home#/security_credentials",
        _ => "https://dash.cloudflare.com/"
    };

    public static IReadOnlyList<Provider> All { get; } =
        new[] { Provider.CloudflareR2, Provider.BackblazeB2, Provider.AmazonS3 };
}

// MARK: - Account

/// Non-secret account configuration. The secret access key lives in the Windows
/// Credential Manager, keyed by `Id` — see `CredentialStore.cs`.
public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Provider Provider { get; set; } = Provider.CloudflareR2;

    /// Cloudflare account ID (R2 only) — used to derive the endpoint.
    public string CloudflareAccountID { get; set; } = "";

    /// Region for B2 (`us-west-004`) or Amazon S3 (`us-east-1`).
    /// (Named for its original B2-only role; kept for stored-JSON compatibility.)
    public string B2Region { get; set; } = "";

    /// Optional custom S3-compatible endpoint; overrides the derived one when set.
    public string CustomEndpoint { get; set; } = "";

    public string AccessKeyID { get; set; } = "";

    public string SigningRegion => Provider switch
    {
        Provider.CloudflareR2 => "auto",
        Provider.BackblazeB2 => string.IsNullOrEmpty(B2Region) ? "us-west-004" : B2Region,
        Provider.AmazonS3 => string.IsNullOrEmpty(B2Region) ? "us-east-1" : B2Region,
        _ => "auto"
    };

    public Uri? EndpointUrl
    {
        get
        {
            var custom = CustomEndpoint.Trim();
            if (custom.Length > 0)
            {
                var raw = custom;
                if (!raw.Contains("://")) raw = "https://" + raw;
                if (raw.EndsWith("/")) raw = raw[..^1];
                return Uri.TryCreate(raw, UriKind.Absolute, out var parsed) ? parsed : null;
            }

            switch (Provider)
            {
                case Provider.CloudflareR2:
                {
                    var acct = CloudflareAccountID.Trim();
                    if (acct.Length == 0) return null;
                    return new Uri($"https://{acct}.r2.cloudflarestorage.com");
                }
                case Provider.BackblazeB2:
                {
                    var region = B2Region.Trim();
                    if (region.Length == 0) return null;
                    return new Uri($"https://s3.{region}.backblazeb2.com");
                }
                default:
                {
                    var region = B2Region.Trim();
                    if (region.Length == 0) region = "us-east-1";
                    return new Uri($"https://s3.{region}.amazonaws.com");
                }
            }
        }
    }

    public bool IsConfigured => EndpointUrl != null && AccessKeyID.Length > 0;

    public Account Clone() => (Account)MemberwiseClone();

    /// Name to show in menus and headers — falls back to the provider name.
    public string DisplayLabel => string.IsNullOrWhiteSpace(Name) ? Provider.DisplayName() : Name;
}

// MARK: - Bucket

public sealed class Bucket
{
    public Bucket(string name, DateTime? creationDate = null)
    {
        Name = name;
        CreationDate = creationDate;
    }

    public string Name { get; }
    public DateTime? CreationDate { get; }
    public string Id => Name;
}

// MARK: - Remote object

/// A file or folder (common prefix) inside a bucket.
public sealed class RemoteObject
{
    public string Key { get; set; } = "";
    public long Size { get; set; }
    public DateTime? LastModified { get; set; }
    public string? ETag { get; set; }
    public string? StorageClass { get; set; }
    public bool IsFolder { get; set; }

    public string Id => (IsFolder ? "d:" : "f:") + Key;

    /// Display name — the last path component of the key.
    public string Name
    {
        get
        {
            var trimmed = IsFolder && Key.EndsWith("/") ? Key[..^1] : Key;
            var index = trimmed.LastIndexOf('/');
            return index >= 0 ? trimmed[(index + 1)..] : trimmed;
        }
    }

    public string FileExtension
    {
        get
        {
            if (IsFolder) return "";
            var ext = Path.GetExtension(Name);
            return ext.Length > 1 ? ext[1..].ToLowerInvariant() : "";
        }
    }

    public DateTime SortDate => LastModified ?? DateTime.MinValue;

    public bool IsImage => FileKinds.IsImage(FileExtension);
    public bool IsVideo => FileKinds.IsVideo(FileExtension);
    public bool IsAudio => FileKinds.IsAudio(FileExtension);
    public bool IsText => FileKinds.IsText(FileExtension);

    public string SymbolName
    {
        get
        {
            if (IsFolder) return "folder.fill";
            if (IsImage) return "photo";
            if (IsVideo) return "film";
            if (IsAudio) return "waveform";
            var ext = FileExtension;
            if (ext == "pdf") return "doc.richtext";
            if (FileKinds.IsArchive(ext)) return "doc.zipper";
            if (FileKinds.IsSourceCode(ext)) return "chevron.left.forwardslash.chevron.right";
            if (FileKinds.IsText(ext)) return "doc.text";
            return "doc";
        }
    }

    public string FormattedSize => IsFolder ? "—" : ByteFormat.String(Size);

    /// Best-effort MIME type, used when relaying an object to another account.
    public string? ContentType => FileKinds.MimeType(FileExtension);
}

// MARK: - Object metadata (HEAD)

public sealed class ObjectMetadata
{
    public string? ContentType { get; set; }
    public long? ContentLength { get; set; }
    public string? LastModified { get; set; }
    public string? ETag { get; set; }
    public string? StorageClass { get; set; }
    public Dictionary<string, string> Custom { get; } = new();  // x-amz-meta-*
}

// MARK: - Bucket analytics

public sealed class ExtensionStat
{
    public ExtensionStat(string ext, int count, long totalSize)
    {
        Ext = ext;
        Count = count;
        TotalSize = totalSize;
    }

    public string Ext { get; }
    public int Count { get; }
    public long TotalSize { get; }
    public string Id => Ext;
    public string FormattedSize => ByteFormat.String(TotalSize);
}

public sealed class BucketStats
{
    public required string Bucket { get; init; }
    public required int ObjectCount { get; init; }
    public required long TotalSize { get; init; }
    public required IReadOnlyList<ExtensionStat> ByExtension { get; init; }
    public required IReadOnlyList<RemoteObject> LargestObjects { get; init; }
    public DateTime? NewestModified { get; init; }
    public required DateTime AnalyzedAt { get; init; }

    public string FormattedSize => ByteFormat.String(TotalSize);
}

// MARK: - Browser display settings

public enum ViewMode { Grid, List }

public static class ViewModeExtensions
{
    public static string RawValue(this ViewMode m) => m == ViewMode.Grid ? "grid" : "list";
    public static ViewMode FromRawValue(string? raw) => raw == "list" ? ViewMode.List : ViewMode.Grid;
    public static string SymbolName(this ViewMode m) => m == ViewMode.Grid ? "square.grid.2x2" : "list.bullet";
    public static string Label(this ViewMode m) => m == ViewMode.Grid ? "Grid" : "List";
    public static IReadOnlyList<ViewMode> All { get; } = new[] { ViewMode.Grid, ViewMode.List };
}

public enum SortField { Name, Size, Date, Kind }

public static class SortFieldExtensions
{
    public static string RawValue(this SortField f) => f switch
    {
        SortField.Name => "name",
        SortField.Size => "size",
        SortField.Date => "date",
        SortField.Kind => "kind",
        _ => "name"
    };

    public static SortField FromRawValue(string? raw) => raw switch
    {
        "size" => SortField.Size,
        "date" => SortField.Date,
        "kind" => SortField.Kind,
        _ => SortField.Name
    };

    public static string Label(this SortField f) => f switch
    {
        SortField.Name => "Name",
        SortField.Size => "Size",
        SortField.Date => "Date Modified",
        SortField.Kind => "Kind",
        _ => "Name"
    };

    public static IReadOnlyList<SortField> All { get; } =
        new[] { SortField.Name, SortField.Size, SortField.Date, SortField.Kind };
}

// MARK: - Helpers

/// Byte counts formatted the way macOS's ByteCountFormatter(.file) does:
/// decimal units (1 KB = 1000 bytes), one fractional digit above KB.
public static class ByteFormat
{
    private static readonly string[] Units = { "bytes", "KB", "MB", "GB", "TB", "PB" };

    public static string String(long bytes)
    {
        if (bytes < 1000) return $"{bytes} {(bytes == 1 ? "byte" : "bytes")}";
        double value = bytes;
        var unit = 0;
        while (value >= 1000 && unit < Units.Length - 1)
        {
            value /= 1000;
            unit++;
        }
        var digits = value >= 100 ? 0 : 1;
        return value.ToString("N" + digits, CultureInfo.CurrentCulture) + " " + Units[unit];
    }
}

/// Extension-driven file classification — the Windows stand-in for
/// UniformTypeIdentifiers on macOS.
public static class FileKinds
{
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "bmp", "tif", "tiff", "webp", "heic", "heif",
        "ico", "svg", "avif", "jfif"
    };

    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "m4v", "mov", "avi", "mkv", "webm", "wmv", "flv", "mpg", "mpeg", "3gp", "ogv"
    };

    private static readonly HashSet<string> Audios = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "m4a", "aac", "wav", "flac", "ogg", "oga", "wma", "aiff", "aif", "opus"
    };

    private static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "gz", "tgz", "bz2", "xz", "7z", "rar", "tar", "zst", "dmg", "iso", "cab"
    };

    private static readonly HashSet<string> SourceCode = new(StringComparer.OrdinalIgnoreCase)
    {
        "swift", "cs", "c", "h", "cpp", "hpp", "cc", "m", "mm", "java", "kt", "go", "rs",
        "py", "rb", "php", "pl", "sh", "bash", "zsh", "ps1", "bat", "cmd", "js", "mjs",
        "cjs", "ts", "tsx", "jsx", "vue", "css", "scss", "less", "html", "htm", "xml",
        "json", "yaml", "yml", "toml", "sql", "r", "lua", "dart", "scala", "ex", "exs"
    };

    private static readonly HashSet<string> Texts = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "md", "markdown", "log", "csv", "tsv", "ini", "cfg", "conf", "env",
        "rtf", "srt", "vtt", "properties", "gitignore", "editorconfig"
    };

    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["gif"] = "image/gif",
        ["bmp"] = "image/bmp",
        ["webp"] = "image/webp",
        ["svg"] = "image/svg+xml",
        ["tif"] = "image/tiff",
        ["tiff"] = "image/tiff",
        ["ico"] = "image/x-icon",
        ["heic"] = "image/heic",
        ["avif"] = "image/avif",
        ["pdf"] = "application/pdf",
        ["txt"] = "text/plain",
        ["md"] = "text/markdown",
        ["csv"] = "text/csv",
        ["html"] = "text/html",
        ["htm"] = "text/html",
        ["css"] = "text/css",
        ["js"] = "text/javascript",
        ["mjs"] = "text/javascript",
        ["json"] = "application/json",
        ["xml"] = "application/xml",
        ["yaml"] = "application/yaml",
        ["yml"] = "application/yaml",
        ["zip"] = "application/zip",
        ["gz"] = "application/gzip",
        ["tar"] = "application/x-tar",
        ["7z"] = "application/x-7z-compressed",
        ["rar"] = "application/vnd.rar",
        ["mp3"] = "audio/mpeg",
        ["m4a"] = "audio/mp4",
        ["wav"] = "audio/wav",
        ["flac"] = "audio/flac",
        ["ogg"] = "audio/ogg",
        ["aac"] = "audio/aac",
        ["opus"] = "audio/opus",
        ["mp4"] = "video/mp4",
        ["m4v"] = "video/x-m4v",
        ["mov"] = "video/quicktime",
        ["avi"] = "video/x-msvideo",
        ["mkv"] = "video/x-matroska",
        ["webm"] = "video/webm",
        ["doc"] = "application/msword",
        ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ["xls"] = "application/vnd.ms-excel",
        ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ["ppt"] = "application/vnd.ms-powerpoint",
        ["pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ["exe"] = "application/vnd.microsoft.portable-executable",
        ["msi"] = "application/x-msdownload"
    };

    public static bool IsImage(string ext) => ext.Length > 0 && Images.Contains(ext);
    public static bool IsVideo(string ext) => ext.Length > 0 && Videos.Contains(ext);
    public static bool IsAudio(string ext) => ext.Length > 0 && Audios.Contains(ext);
    public static bool IsArchive(string ext) => ext.Length > 0 && Archives.Contains(ext);
    public static bool IsSourceCode(string ext) => ext.Length > 0 && SourceCode.Contains(ext);
    public static bool IsText(string ext) =>
        ext.Length > 0 && (Texts.Contains(ext) || SourceCode.Contains(ext));

    /// True for formats the in-app previewer can decode directly.
    public static bool IsPreviewableImage(string ext) =>
        ext.Length > 0 && ext.ToLowerInvariant() is "png" or "jpg" or "jpeg" or "bmp" or "gif" or "webp";

    public static string? MimeType(string ext) =>
        ext.Length > 0 && Mime.TryGetValue(ext, out var mime) ? mime : null;

    public static string MimeTypeOrDefault(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Length > 1) ext = ext[1..];
        return MimeType(ext) ?? "application/octet-stream";
    }
}
