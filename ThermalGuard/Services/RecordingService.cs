using LibVLCSharp.Shared;
using System.IO;
using ThermalGuard.Models;

namespace ThermalGuard.Services;

public sealed class RecordingService : IAsyncDisposable
{
    private readonly LibVLC _libVlc;
    private readonly RecordingSettings _settings;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<Camera, (CancellationTokenSource Stop, Task Task)> _workers = [];
    private readonly SemaphoreSlim _storageGate = new(1, 1);

    public RecordingService(LibVLC libVlc, RecordingSettings settings) =>
        (_libVlc, _settings) = (libVlc, settings);

    public void Start(IEnumerable<Camera> cameras)
    {
        if (!_settings.Enabled) return;
        Directory.CreateDirectory(_settings.OutputDirectory);
        foreach (var camera in cameras) AddCamera(camera);
    }

    public void AddCamera(Camera camera)
    {
        if (!_settings.Enabled || camera.SourceType != "熱像儀" || _workers.ContainsKey(camera)) return;
        var stop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _workers[camera] = (stop, RunCameraAsync(camera, stop.Token));
    }

    public bool Records(Camera camera) => _settings.Enabled && camera.SourceType == "熱像儀";

    public Media CreateMedia(Camera camera)
    {
        var media = new Media(_libVlc, new Uri(camera.Rtsp));
        if (!Records(camera)) return media;

        AddCamera(camera);
        var temporaryDirectory = Path.Combine(_settings.OutputDirectory, "temp", camera.Id);
        Directory.CreateDirectory(temporaryDirectory);
        var session = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
        var indexPath = ToVlcPath(Path.Combine(temporaryDirectory, $"{session}.m3u8"));
        var segmentPattern = ToVlcPath(Path.Combine(temporaryDirectory, $"{session}-######.ts"));
        media.AddOption($":sout=#duplicate{{dst=display,dst=std{{access=livehttp{{seglen={_settings.SegmentSeconds},numsegs=5,delsegs=false,index='{indexPath}',index-url='{session}-######.ts'}},mux=ts{{use-key-frames}},dst='{segmentPattern}'}}}}");
        media.AddOption(":sout-all");
        return media;
    }

    public async Task RemoveCameraAsync(Camera camera)
    {
        if (!_workers.Remove(camera, out var worker)) return;
        await worker.Stop.CancelAsync();
        try { await worker.Task; } catch (OperationCanceledException) { }
        worker.Stop.Dispose();
    }

    public async Task RefreshCameraAsync(Camera camera)
    {
        await RemoveCameraAsync(camera);
        AddCamera(camera);
    }

    private async Task RunCameraAsync(Camera camera, CancellationToken token)
    {
        var temporaryDirectory = Path.Combine(_settings.OutputDirectory, "temp", camera.Id);
        var eventDirectory = Path.Combine(_settings.OutputDirectory, "events", camera.Id);
        Directory.CreateDirectory(temporaryDirectory);
        Directory.CreateDirectory(eventDirectory);
        DateTime? eventEndedAt = null;
        var eventActive = false;

        while (!token.IsCancellationRequested)
        {
            if (!await EnsureStorageSpaceAsync(0, token))
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.SegmentSeconds), token);
                continue;
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);

                var triggered = camera.Status is "預警" or "警報" or "溫升";
                if (triggered)
                {
                    eventActive = true;
                    eventEndedAt = null;
                }
                else if (eventActive)
                {
                    eventEndedAt ??= DateTime.UtcNow;
                    var finalizeAfter = TimeSpan.FromSeconds(
                        _settings.PostEventSeconds + _settings.SegmentSeconds);
                    if (DateTime.UtcNow - eventEndedAt >= finalizeAfter &&
                        await FinalizeEventAsync(camera, temporaryDirectory, eventDirectory, token))
                    {
                        eventActive = false;
                        eventEndedAt = null;
                    }
                }

                if (!eventActive) CleanupExpiredSegments(temporaryDirectory);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
    }

    private async Task<bool> FinalizeEventAsync(Camera camera, string temporaryDirectory,
        string eventDirectory, CancellationToken token)
    {
        var segments = GetCompletedSegments(temporaryDirectory);
        if (segments.Length == 0) return true;
        var requiredBytes = segments.Sum(path => new FileInfo(path).Length);
        if (!await EnsureStorageSpaceAsync(requiredBytes, token)) return false;

        var destination = Path.Combine(eventDirectory,
            $"{camera.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.ts");
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            foreach (var segment in segments)
            {
                await using var input = new FileStream(segment, FileMode.Open, FileAccess.Read, FileShare.Read);
                await input.CopyToAsync(output, token);
            }
        }
        foreach (var segment in segments) File.Delete(segment);
        return true;
    }

    private async Task<bool> EnsureStorageSpaceAsync(long requiredBytes, CancellationToken token)
    {
        await _storageGate.WaitAsync(token);
        try
        {
            var reservedBytes = (long)_settings.ReservedSpaceGb * 1024 * 1024 * 1024;
            long AvailableBytes()
            {
                var root = Path.GetPathRoot(Path.GetFullPath(_settings.OutputDirectory));
                if (string.IsNullOrWhiteSpace(root)) throw new IOException("無法判斷錄影位置所在磁碟");
                return new DriveInfo(root).AvailableFreeSpace;
            }

            if (AvailableBytes() - requiredBytes >= reservedBytes) return true;
            if (!_settings.OverwriteOldestEvents) return false;

            var eventsDirectory = Path.Combine(_settings.OutputDirectory, "events");
            if (!Directory.Exists(eventsDirectory)) return false;
            foreach (var eventFile in Directory.GetFiles(eventsDirectory, "*.ts", SearchOption.AllDirectories)
                         .OrderBy(File.GetLastWriteTimeUtc))
            {
                File.Delete(eventFile);
                if (AvailableBytes() - requiredBytes >= reservedBytes) return true;
            }
            return false;
        }
        finally
        {
            _storageGate.Release();
        }
    }

    private void CleanupExpiredSegments(string directory)
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(_settings.BufferMinutes);
        foreach (var path in GetCompletedSegments(directory))
            if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
    }

    private static string[] GetCompletedSegments(string directory)
    {
        var segments = Directory.GetFiles(directory, "*.ts")
            .Where(path => new FileInfo(path).Length > 0)
            .OrderBy(File.GetLastWriteTimeUtc)
            .ToList();
        if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
        return [.. segments];
    }

    private static string ToVlcPath(string path) => path.Replace('\\', '/').Replace("'", "\\'");

    public Task DeleteTemporaryFilesAsync() => DeleteTemporaryFilesAsync(_settings.OutputDirectory);

    public static async Task DeleteTemporaryFilesAsync(string outputDirectoryPath)
    {
        var outputDirectory = Path.GetFullPath(outputDirectoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var temporaryDirectory = Path.GetFullPath(Path.Combine(outputDirectory, "temp"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var requiredPrefix = outputDirectory + Path.DirectorySeparatorChar;
        if (!temporaryDirectory.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(temporaryDirectory).Equals("temp", StringComparison.OrdinalIgnoreCase))
            throw new IOException("錄影暫存路徑不正確，已取消刪除");

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (!Directory.Exists(temporaryDirectory)) return;
            try
            {
                await Task.Run(() => Directory.Delete(temporaryDirectory, true));
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                await Task.Delay(250);
            }
            catch (UnauthorizedAccessException) when (attempt < 3)
            {
                await Task.Delay(250);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        var workers = _workers.Values.ToArray();
        foreach (var worker in workers) await worker.Stop.CancelAsync();
        foreach (var worker in workers)
        {
            try { await worker.Task; } catch (OperationCanceledException) { }
            worker.Stop.Dispose();
        }
        _workers.Clear();
        _storageGate.Dispose();
        _stop.Dispose();
    }
}
