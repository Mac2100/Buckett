using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Buckett.Services;

/// Local record of upload activity per day, powering the Statistics heatmap.
/// Purely local — nothing is sent anywhere.
public sealed class UploadHistory : ObservableObject
{
    public static UploadHistory Shared { get; } = new();

    public sealed class Day
    {
        public int files { get; set; }
        public long bytes { get; set; }
    }

    /// Keyed by "yyyy-MM-dd" (local time zone).
    private Dictionary<string, Day> _days = new();

    public IReadOnlyDictionary<string, Day> Days => _days;

    private static string FilePath => Path.Combine(AppPaths.SupportDirectory, "upload-history.json");

    private UploadHistory()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var decoded = JsonSerializer.Deserialize<Dictionary<string, Day>>(json);
                if (decoded != null) _days = decoded;
            }
        }
        catch (Exception error)
        {
            Log.Warn($"failed to load upload history: {error.Message}");
        }
    }

    public static string Key(DateTime date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public void Record(long bytes, DateTime? date = null)
    {
        var key = Key(date ?? DateTime.Now);
        if (!_days.TryGetValue(key, out var day))
        {
            day = new Day();
            _days[key] = day;
        }
        day.files += 1;
        day.bytes += bytes;
        Save();
        OnPropertyChanged(nameof(Days));
    }

    public Day? DayFor(DateTime date) => _days.TryGetValue(Key(date), out var day) ? day : null;

    /// The last `count` days, oldest first.
    public List<(DateTime Date, Day? Day)> RecentDays(int count)
    {
        var today = DateTime.Today;
        return Enumerable.Range(0, count)
            .Reverse()
            .Select(offset =>
            {
                var date = today.AddDays(-offset);
                return (date, DayFor(date));
            })
            .ToList();
    }

    private void Save()
    {
        try
        {
            AppPaths.EnsureSupportDirectory();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_days));
        }
        catch (Exception error)
        {
            Log.Warn($"failed to save upload history: {error.Message}");
        }
    }
}
