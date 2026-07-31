using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Buckett.Services;

/// User-defined display names for buckets. Aliases are purely cosmetic — every
/// API request still uses the real bucket name. Keyed by "accountUUID|bucket".
public sealed class BucketAliases : ObservableObject
{
    public static BucketAliases Shared { get; } = new();

    private Dictionary<string, string> _aliases = new();

    public IReadOnlyDictionary<string, string> Aliases => _aliases;

    private static string FilePath => Path.Combine(AppPaths.SupportDirectory, "bucket-aliases.json");

    private BucketAliases()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var decoded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (decoded != null) _aliases = decoded;
            }
        }
        catch (Exception error)
        {
            Log.Warn($"failed to load bucket aliases: {error.Message}");
        }
    }

    private static string Key(Guid accountID, string bucket) =>
        $"{accountID.ToString("D").ToUpperInvariant()}|{bucket}";

    public string? Alias(Guid accountID, string bucket) =>
        _aliases.TryGetValue(Key(accountID, bucket), out var alias) ? alias : null;

    /// Alias when set, otherwise the real bucket name.
    public string DisplayName(Guid accountID, string bucket) => Alias(accountID, bucket) ?? bucket;

    /// Sets (or clears with null/empty) the alias for a bucket.
    public void SetAlias(string? alias, Guid accountID, string bucket)
    {
        var key = Key(accountID, bucket);
        var trimmed = alias?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            _aliases.Remove(key);
        }
        else
        {
            _aliases[key] = trimmed;
        }
        Save();
        OnPropertyChanged(nameof(Aliases));
    }

    private void Save()
    {
        try
        {
            AppPaths.EnsureSupportDirectory();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_aliases));
        }
        catch (Exception error)
        {
            Log.Warn($"failed to save bucket aliases: {error.Message}");
        }
    }
}
