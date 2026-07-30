using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Buckett.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Buckett.Services;

public enum TransferStateKind { Queued, Running, Completed, Cancelled, Failed }

public readonly record struct TransferState(TransferStateKind Kind, string? Message = null)
{
    public static readonly TransferState Queued = new(TransferStateKind.Queued);
    public static readonly TransferState Running = new(TransferStateKind.Running);
    public static readonly TransferState Completed = new(TransferStateKind.Completed);
    public static readonly TransferState Cancelled = new(TransferStateKind.Cancelled);
    public static TransferState Failed(string message) => new(TransferStateKind.Failed, message);

    public bool IsFinished => Kind is TransferStateKind.Completed
        or TransferStateKind.Cancelled or TransferStateKind.Failed;
}

public enum TransferKind { Upload, Download }

public sealed class TransferTask : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();
    public TransferKind Kind { get; }
    public string Bucket { get; }
    public string Key { get; }
    public string LocalPath { get; }
    public S3Client Client { get; }

    private TransferState _state = TransferState.Queued;
    private long _transferredBytes;
    private long _totalBytes;
    private double _bytesPerSecond;
    private (int Current, int Total)? _partProgress;

    private DateTime? _lastSampleTime;
    private long _lastSampleBytes;

    internal CancellationTokenSource? Cancellation;

    public TransferTask(
        TransferKind kind, string bucket, string key, string localPath, long totalBytes, S3Client client)
    {
        Kind = kind;
        Bucket = bucket;
        Key = key;
        LocalPath = localPath;
        _totalBytes = totalBytes;
        Client = client;
    }

    public TransferState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsQueued));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsCancelled));
                OnPropertyChanged(nameof(IsFailed));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(CanRetry));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CompletedText));
                OnPropertyChanged(nameof(ShowCopyLink));
                OnPropertyChanged(nameof(ShowReveal));
            }
        }
    }

    public long TransferredBytes
    {
        get => _transferredBytes;
        set
        {
            if (!SetProperty(ref _transferredBytes, value)) return;
            UpdateSpeed();
            OnPropertyChanged(nameof(FractionCompleted));
            OnPropertyChanged(nameof(PercentText));
            OnPropertyChanged(nameof(ProgressText));
        }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set
        {
            if (!SetProperty(ref _totalBytes, value)) return;
            OnPropertyChanged(nameof(FractionCompleted));
            OnPropertyChanged(nameof(FormattedTotal));
            OnPropertyChanged(nameof(PercentText));
            OnPropertyChanged(nameof(ProgressText));
        }
    }

    /// Smoothed transfer rate in bytes/second while running.
    public double BytesPerSecond
    {
        get => _bytesPerSecond;
        set
        {
            if (SetProperty(ref _bytesPerSecond, value)) OnPropertyChanged(nameof(SpeedText));
        }
    }

    /// (current part, total parts) for multipart uploads.
    public (int Current, int Total)? PartProgress
    {
        get => _partProgress;
        set
        {
            if (SetProperty(ref _partProgress, value))
            {
                OnPropertyChanged(nameof(PartText));
                OnPropertyChanged(nameof(HasPartProgress));
            }
        }
    }

    private void UpdateSpeed()
    {
        var now = DateTime.UtcNow;
        if (_lastSampleTime is not { } last)
        {
            _lastSampleTime = now;
            _lastSampleBytes = _transferredBytes;
            return;
        }
        var elapsed = (now - last).TotalSeconds;
        if (elapsed < 0.4) return;
        var delta = (double)(_transferredBytes - _lastSampleBytes);
        var instant = Math.Max(0, delta / elapsed);
        BytesPerSecond = BytesPerSecond == 0 ? instant : BytesPerSecond * 0.6 + instant * 0.4;
        _lastSampleTime = now;
        _lastSampleBytes = _transferredBytes;
    }

    public string DisplayName => Kind == TransferKind.Upload
        ? Path.GetFileName(LocalPath)
        : Key[(Key.LastIndexOf('/') + 1)..];

    public double FractionCompleted =>
        TotalBytes > 0 ? Math.Min(1, (double)TransferredBytes / TotalBytes) : 0;

    public string SymbolName => Kind == TransferKind.Upload ? "arrow.up.circle" : "arrow.down.circle";

    public bool IsQueued => State.Kind == TransferStateKind.Queued;
    public bool IsRunning => State.Kind == TransferStateKind.Running;
    public bool IsCompleted => State.Kind == TransferStateKind.Completed;
    public bool IsCancelled => State.Kind == TransferStateKind.Cancelled;
    public bool IsFailed => State.Kind == TransferStateKind.Failed;
    public bool CanCancel => State.Kind is TransferStateKind.Queued or TransferStateKind.Running;
    public bool CanRetry => State.Kind is TransferStateKind.Failed or TransferStateKind.Cancelled;
    public bool ShowCopyLink => IsCompleted && Kind == TransferKind.Upload;
    public bool ShowReveal => IsCompleted && Kind == TransferKind.Download;
    public bool HasPartProgress => PartProgress != null;

    public string FormattedTotal => ByteFormat.String(TotalBytes);
    public string PercentText => $"{(int)(FractionCompleted * 100)}%";
    public string ProgressText => $"{ByteFormat.String(TransferredBytes)} / {ByteFormat.String(TotalBytes)}";
    public string SpeedText => BytesPerSecond > 1 ? $"{ByteFormat.String((long)BytesPerSecond)}/s" : "";
    public string PartText => PartProgress is { } p ? $"Parts {p.Current} / {p.Total}" : "";

    public string StatusText => State.Kind switch
    {
        TransferStateKind.Queued => "Queued",
        TransferStateKind.Cancelled => "Cancelled — retrying resumes multipart uploads",
        TransferStateKind.Failed => State.Message ?? "Failed",
        _ => ""
    };

    public string CompletedText => Kind == TransferKind.Upload
        ? $"Uploaded to {Bucket}/{Key}"
        : $"Saved to {LocalPath}";
}

// MARK: - Resumable multipart records

/// On-disk record of an in-flight multipart upload, so it can be resumed after
/// a failure, cancellation, or app relaunch.
public sealed class ResumableRecord
{
    public string uploadID { get; set; } = "";
    public string bucket { get; set; } = "";
    public string key { get; set; } = "";
    public string localPath { get; set; } = "";
    public long fileSize { get; set; }
    public long partSize { get; set; }
    /// part number (as string) → ETag
    public Dictionary<string, string> completedParts { get; set; } = new();
}

public static class ResumableStore
{
    public static string Directory => AppPaths.ResumableDirectory;

    public static string RecordPath(string bucket, string key, string localPath, long fileSize)
    {
        var identity = $"{bucket}|{key}|{localPath}|{fileSize}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant()[..32];
        return Path.Combine(Directory, hash + ".json");
    }

    public static ResumableRecord? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ResumableRecord>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public static void Save(ResumableRecord record, string path)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(record));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error)
        {
            Log.Warn($"could not checkpoint upload: {error.Message}");
        }
    }

    public static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}

// MARK: - Transfer manager

public sealed class TransferManager : ObservableObject
{
    /// Raised (on the UI thread) when an upload finishes, carrying the bucket name.
    public event Action<string>? TransferCompleted;

    public ObservableCollection<TransferTask> Tasks { get; } = new();

    private readonly List<TransferTask> _pending = new();
    private int _running;

    public int MaxConcurrent => Settings.Shared.MaxConcurrentTransfers;

    public int ActiveCount => Tasks.Count(task => !task.State.IsFinished);

    public bool HasFinished => Tasks.Any(task => task.State.IsFinished);

    // MARK: Enqueue

    public void EnqueueUpload(string filePath, string bucket, string key, S3Client client)
    {
        long size = 0;
        try { size = new FileInfo(filePath).Length; } catch { /* reported when the task runs */ }

        var task = new TransferTask(TransferKind.Upload, bucket, key, filePath, size, client);
        Add(task);
    }

    public void EnqueueDownload(RemoteObject remote, string bucket, string destination, S3Client client)
    {
        var task = new TransferTask(
            TransferKind.Download, bucket, remote.Key, destination, remote.Size, client);
        Add(task);
    }

    private void Add(TransferTask task)
    {
        Tasks.Insert(0, task);
        _pending.Add(task);
        NotifyCounts();
        Pump();
    }

    public void Retry(TransferTask task)
    {
        if (!task.State.IsFinished || task.State.Kind == TransferStateKind.Completed) return;
        task.State = TransferState.Queued;
        task.TransferredBytes = 0;
        _pending.Add(task);
        NotifyCounts();
        Pump();
    }

    public void Cancel(TransferTask task)
    {
        if (task.State.Kind == TransferStateKind.Queued)
        {
            _pending.RemoveAll(candidate => candidate.Id == task.Id);
            task.State = TransferState.Cancelled;
            NotifyCounts();
        }
        else
        {
            task.Cancellation?.Cancel();
        }
    }

    public void ClearFinished()
    {
        foreach (var task in Tasks.Where(candidate => candidate.State.IsFinished).ToList())
        {
            Tasks.Remove(task);
        }
        NotifyCounts();
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(HasFinished));
    }

    // MARK: Scheduling

    private void Pump()
    {
        while (_running < MaxConcurrent && _pending.Count > 0)
        {
            var task = _pending[0];
            _pending.RemoveAt(0);
            if (task.State.Kind != TransferStateKind.Queued) continue;

            _running++;
            task.State = TransferState.Running;
            task.Cancellation = new CancellationTokenSource();
            _ = RunAsync(task);
        }
        NotifyCounts();
    }

    private async Task RunAsync(TransferTask task)
    {
        var token = task.Cancellation?.Token ?? CancellationToken.None;
        try
        {
            if (task.Kind == TransferKind.Upload)
            {
                await RunUploadAsync(task, token).ConfigureAwait(true);
            }
            else
            {
                await RunDownloadAsync(task, token).ConfigureAwait(true);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                task.State = TransferState.Completed;
                task.TransferredBytes = task.TotalBytes;
                if (task.Kind == TransferKind.Upload)
                {
                    UploadHistory.Shared.Record(task.TotalBytes);
                    TransferCompleted?.Invoke(task.Bucket);
                }
            });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => task.State = TransferState.Cancelled);
        }
        catch (Exception error)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                task.State = TransferState.Failed(error.Message);
                Notifier.Shared.Post(
                    Notifier.Event.TransferFailed,
                    "Transfer failed",
                    $"{task.DisplayName}: {error.Message}");
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                task.BytesPerSecond = 0;
                task.PartProgress = null;
                task.Cancellation?.Dispose();
                task.Cancellation = null;
                _running--;
                Pump();

                if (ActiveCount == 0 && Tasks.Any(candidate => candidate.IsCompleted))
                {
                    ToastCenter.Shared.Show("All transfers finished");
                    var completed = Tasks.Count(candidate => candidate.IsCompleted);
                    Notifier.Shared.Post(
                        Notifier.Event.TransfersComplete,
                        "Transfers finished",
                        $"{completed} transfer{(completed == 1 ? "" : "s")} completed.");
                }
            });
        }
    }

    // MARK: Download

    private static Task RunDownloadAsync(TransferTask task, CancellationToken token) =>
        task.Client.DownloadObjectAsync(
            task.Bucket,
            task.Key,
            task.LocalPath,
            (transferred, total) => Dispatcher.UIThread.Post(() =>
            {
                task.TransferredBytes = transferred;
                if (total > 0) task.TotalBytes = total;
            }),
            token);

    // MARK: Upload

    private static async Task RunUploadAsync(TransferTask task, CancellationToken token)
    {
        var fileSize = new FileInfo(task.LocalPath).Length;
        await Dispatcher.UIThread.InvokeAsync(() => task.TotalBytes = fileSize);

        if (fileSize >= S3Client.MultipartThreshold)
        {
            await RunMultipartUploadAsync(task, fileSize, token).ConfigureAwait(false);
        }
        else
        {
            var data = await File.ReadAllBytesAsync(task.LocalPath, token).ConfigureAwait(false);
            await task.Client.PutObjectAsync(
                task.Bucket,
                task.Key,
                data,
                FileKinds.MimeTypeOrDefault(task.LocalPath),
                (sent, _) => Dispatcher.UIThread.Post(() => task.TransferredBytes = sent),
                token).ConfigureAwait(false);
        }
    }

    /// Multipart upload that persists progress to disk. If a record for the same
    /// file + destination exists, previously completed parts are skipped.
    private static async Task RunMultipartUploadAsync(
        TransferTask task, long fileSize, CancellationToken token)
    {
        var client = task.Client;
        var bucket = task.Bucket;
        var key = task.Key;
        var filePath = task.LocalPath;
        var contentType = FileKinds.MimeTypeOrDefault(filePath);
        var recordPath = ResumableStore.RecordPath(bucket, key, filePath, fileSize);

        var record = ResumableStore.Load(recordPath);
        if (record == null || record.fileSize != fileSize)
        {
            var uploadID = await client
                .CreateMultipartUploadAsync(bucket, key, contentType, token)
                .ConfigureAwait(false);
            record = new ResumableRecord
            {
                uploadID = uploadID,
                bucket = bucket,
                key = key,
                localPath = filePath,
                fileSize = fileSize,
                partSize = S3Client.MultipartPartSize
            };
            ResumableStore.Save(record, recordPath);
        }

        var partSize = record.partSize > 0 ? record.partSize : S3Client.MultipartPartSize;
        var partCount = (int)((fileSize + partSize - 1) / partSize);

        long PartLength(int part) => Math.Min(partSize, fileSize - (long)(part - 1) * partSize);

        long CompletedBytes() => record.completedParts.Keys
            .Select(number => int.TryParse(number, out var parsed) ? parsed : 0)
            .Where(number => number > 0)
            .Sum(PartLength);

        var baseCompleted = CompletedBytes();
        await Dispatcher.UIThread.InvokeAsync(() => task.TransferredBytes = baseCompleted);

        await using var handle = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);

        for (var part = 1; part <= partCount; part++)
        {
            token.ThrowIfCancellationRequested();
            if (record.completedParts.ContainsKey(part.ToString())) continue;

            var currentPart = part;
            await Dispatcher.UIThread.InvokeAsync(
                () => task.PartProgress = (currentPart, partCount));

            var offset = (long)(part - 1) * partSize;
            var length = (int)PartLength(part);
            handle.Seek(offset, SeekOrigin.Begin);
            var data = new byte[length];
            await handle.ReadExactlyAsync(data, token).ConfigureAwait(false);

            var alreadyDone = CompletedBytes();

            string etag;
            try
            {
                etag = await client.UploadPartAsync(
                    bucket, key, record.uploadID, part, data,
                    (sent, _) => Dispatcher.UIThread.Post(
                        () => task.TransferredBytes = alreadyDone + sent),
                    token).ConfigureAwait(false);
            }
            catch (S3Exception error) when (error.IsNoSuchUpload)
            {
                // Stale record — the multipart upload no longer exists. Start over once.
                ResumableStore.Delete(recordPath);
                var uploadID = await client
                    .CreateMultipartUploadAsync(bucket, key, contentType, token)
                    .ConfigureAwait(false);
                record = new ResumableRecord
                {
                    uploadID = uploadID,
                    bucket = bucket,
                    key = key,
                    localPath = filePath,
                    fileSize = fileSize,
                    partSize = partSize
                };
                ResumableStore.Save(record, recordPath);
                handle.Seek(offset, SeekOrigin.Begin);
                await handle.ReadExactlyAsync(data, token).ConfigureAwait(false);
                etag = await client
                    .UploadPartAsync(bucket, key, record.uploadID, part, data, token: token)
                    .ConfigureAwait(false);
            }

            record.completedParts[part.ToString()] = etag;
            ResumableStore.Save(record, recordPath);
        }

        var parts = record.completedParts
            .Select(entry => int.TryParse(entry.Key, out var number)
                ? new CompletedPart(number, entry.Value)
                : default)
            .Where(part => part.PartNumber > 0)
            .ToList();

        await client
            .CompleteMultipartUploadAsync(bucket, key, record.uploadID, parts, token)
            .ConfigureAwait(false);
        ResumableStore.Delete(recordPath);
    }
}
