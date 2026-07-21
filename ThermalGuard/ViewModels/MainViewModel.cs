using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using ThermalGuard.Models;
using ThermalGuard.Services;

namespace ThermalGuard.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly DatabaseService _database = new();
    private readonly LibVLC _libVlc = new("--network-caching=300", "--rtsp-tcp", "--no-video-title-show");
    private readonly SemaphoreSlim _playbackRefreshGate = new(1, 1);
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly SystemInfoService _systemInfo = new();
    private readonly EventActionService _eventActions = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<Camera, Task> _reconnectTasks = new();
    private readonly ConcurrentDictionary<Camera, byte> _authenticationErrors = new();
    private readonly ConcurrentDictionary<Camera, byte> _removedCameras = new();
    private readonly ConcurrentDictionary<Camera, string> _lastEventStatuses = new();
    private readonly Dictionary<Camera, CameraEvent> _activeEvents = [];
    private RecordingService? _recording;
    private ModbusTemperatureService? _modbus;
    private string _recordingOutputDirectory = Path.Combine(AppContext.BaseDirectory, "recordings");
    private Camera? _selectedCamera;
    private Camera? _maximizedCamera;
    private int _gridColumns;
    private string _currentTime = "";
    private double _cpuUsage;
    private double _memoryUsage;
    private double _diskUsage;
    private string _memorySummary = "讀取中";
    private string _diskSummary = "讀取中";
    private Task? _disposeTask;
    private bool _suppressAutomaticDisplay;

    public ObservableCollection<Camera> Cameras { get; } = [];
    public ObservableCollection<CameraSlot> DisplayedCameraSlots { get; } = [];
    public ObservableCollection<CameraEvent> Events { get; } = [];
    public Camera? SelectedCamera { get => _selectedCamera; set => Set(ref _selectedCamera, value); }
    public Camera? MaximizedCamera
    {
        get => _maximizedCamera;
        set
        {
            Set(ref _maximizedCamera, value);
            OnPropertyChanged(nameof(EffectiveColumns));
            RefreshDisplayedCameras();
        }
    }
    public int GridColumns
    {
        get => _gridColumns;
        set
        {
            Set(ref _gridColumns, value);
            OnPropertyChanged(nameof(EffectiveColumns));
            RefreshDisplayedCameras();
        }
    }
    public int EffectiveColumns => MaximizedCamera is not null ? 1 : GridColumns > 0 ? GridColumns : Cameras.Count switch { <= 1 => 1, <= 4 => 2, <= 9 => 3, <= 16 => 4, <= 25 => 5, <= 36 => 6, <= 49 => 7, _ => 8 };
    public string CurrentTime { get => _currentTime; private set => Set(ref _currentTime, value); }
    public double CpuUsage { get => _cpuUsage; private set => Set(ref _cpuUsage, value); }
    public double MemoryUsage { get => _memoryUsage; private set => Set(ref _memoryUsage, value); }
    public double DiskUsage { get => _diskUsage; private set => Set(ref _diskUsage, value); }
    public string MemorySummary { get => _memorySummary; private set => Set(ref _memorySummary, value); }
    public string DiskSummary { get => _diskSummary; private set => Set(ref _diskSummary, value); }
    public int OnlineCount => Cameras.Count(x => x.StreamOnline);

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        _libVlc.Log += LibVlcLog;
        Cameras.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(EffectiveColumns));
            RefreshDisplayedCameras();
            OnPropertyChanged(nameof(OnlineCount));
        };
    }

    private void RefreshDisplayedCameras()
    {
        var capacity = MaximizedCamera is null ? EffectiveColumns * EffectiveColumns : 1;
        while (DisplayedCameraSlots.Count > capacity)
            DisplayedCameraSlots.RemoveAt(DisplayedCameraSlots.Count - 1);

        if (MaximizedCamera is not null)
        {
            if (DisplayedCameraSlots.Count == 0) DisplayedCameraSlots.Add(new CameraSlot());
            DisplayedCameraSlots[0].Camera = MaximizedCamera;
            return;
        }

        var displayed = DisplayedCameraSlots
            .Select(slot => slot.Camera)
            .OfType<Camera>()
            .ToHashSet();
        var available = new Queue<Camera>(Cameras.Where(camera => !displayed.Contains(camera)));

        while (DisplayedCameraSlots.Count < capacity)
            DisplayedCameraSlots.Add(new CameraSlot
            {
                Camera = !_suppressAutomaticDisplay && available.Count > 0 ? available.Dequeue() : null
            });

        if (_suppressAutomaticDisplay) return;
        foreach (var slot in DisplayedCameraSlots.Where(slot => slot.Camera is null))
        {
            if (available.Count == 0) break;
            slot.Camera = available.Dequeue();
        }
    }

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        foreach (var camera in await _database.LoadCamerasAsync())
        {
            Track(camera);
            Cameras.Add(camera);
        }
        var eventSettings = (await _database.LoadEventSettingsAsync())
            .ToDictionary(setting => setting.CameraId, StringComparer.OrdinalIgnoreCase);
        foreach (var camera in Cameras)
            if (eventSettings.TryGetValue(camera.Id, out var setting)) camera.ApplyEventSettings(setting);
        _modbus = new ModbusTemperatureService(Cameras);
        await _modbus.ApplyAsync(NormalizeModbusSettings(await _database.LoadModbusSettingsAsync()));
        SelectedCamera = Cameras.FirstOrDefault();
        var recordingSettings = await _database.LoadRecordingAsync();
        _recordingOutputDirectory = recordingSettings.OutputDirectory;
        if (recordingSettings.Enabled)
        {
            _recording = new RecordingService(_libVlc, recordingSettings);
            _recording.Start(Cameras);
            await RestartPlaybackForRecordingSettingsAsync();
        }
        _clock.Tick += (_, _) => UpdateSystemStatus();
        _clock.Start();
        UpdateSystemStatus();
    }

    public IEnumerable<string> AvailableIds() => Enumerable.Range(1, 99).Select(x => $"CH{x:00}").Except(Cameras.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);

    private void UpdateSystemStatus()
    {
        CurrentTime = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
        var snapshot = _systemInfo.Read();
        CpuUsage = snapshot.CpuPercent;
        MemoryUsage = snapshot.MemoryPercent;
        DiskUsage = snapshot.DiskPercent;
        MemorySummary = snapshot.MemoryText;
        DiskSummary = snapshot.DiskText;
        foreach (var camera in Cameras) camera.EvaluateEventStatus(DateTime.UtcNow);
    }

    public Task<RecordingSettings> LoadRecordingSettingsAsync() => _database.LoadRecordingAsync();
    public Task<List<ModbusSettings>> LoadModbusSettingsAsync() => _database.LoadModbusSettingsAsync();
    public Task ExportSettingsAsync(string path) => _database.ExportAsync(path);
    public Task<string> ImportSettingsAsync(string path) => _database.ImportAsync(path);

    public async Task SaveModbusSettingsAsync(IEnumerable<ModbusSettings> settings)
    {
        var values = NormalizeModbusSettings(settings);
        await _database.SaveModbusSettingsAsync(values);
        if (_modbus is not null) await _modbus.ApplyAsync(values);
    }

    private ModbusSettings[] NormalizeModbusSettings(IEnumerable<ModbusSettings> settings)
    {
        var byCamera = settings.ToDictionary(setting => setting.CameraId, StringComparer.OrdinalIgnoreCase);
        return Cameras.Where(camera => camera.SourceType == "熱像儀")
            .GroupBy(GetCameraHost, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var camera = group.First();
                var setting = group.Select(item => byCamera.GetValueOrDefault(item.Id))
                    .FirstOrDefault(item => item is not null);
                return setting is not null ? setting with
                {
                    CameraId = camera.Id,
                    Host = GetCameraHost(camera),
                    SlaveId = 1,
                    RegisterAddress = 0,
                    AlarmRegisterAddress = 4
                }
                : new ModbusSettings(camera.Id, false, GetCameraHost(camera), 502, 1, 0, 4, "唯讀", 0, 1000);
            })
            .ToArray();
    }

    private static string GetCameraHost(Camera camera) =>
        Uri.TryCreate(camera.Rtsp, UriKind.Absolute, out var uri) ? uri.Host : "127.0.0.1";

    public IReadOnlyList<Camera> EventSettingCameras => Cameras;

    public async Task SaveEventSettingsAsync(IEnumerable<EventSettings> settings)
    {
        var values = settings.ToArray();
        await _database.SaveEventSettingsAsync(values);
        var byCamera = values.ToDictionary(setting => setting.CameraId, StringComparer.OrdinalIgnoreCase);
        foreach (var camera in Cameras)
            if (byCamera.TryGetValue(camera.Id, out var setting)) camera.ApplyEventSettings(setting);
    }

    public async Task SaveRecordingSettingsAsync(RecordingSettings settings)
    {
        await _database.SaveRecordingAsync(settings);
        _recordingOutputDirectory = settings.OutputDirectory;
        if (_recording is not null) await _recording.DisposeAsync();
        _recording = null;
        if (settings.Enabled)
        {
            _recording = new RecordingService(_libVlc, settings);
            _recording.Start(Cameras);
        }
        await RestartPlaybackForRecordingSettingsAsync();
    }

    public async Task AddCameraAsync(string id, string name, string rtsp, string sourceType, string imagingType, string streamType)
    {
        id = id.Trim().ToUpperInvariant(); name = name.Trim(); rtsp = rtsp.Trim();
        if (id.Length == 0 || name.Length == 0 || rtsp.Length == 0) throw new ArgumentException("ID、名稱與串流資料不可空白");
        if (!Uri.TryCreate(rtsp, UriKind.Absolute, out var uri) || uri.Scheme is not ("rtsp" or "rtsps")) throw new ArgumentException("網址必須以 rtsp:// 或 rtsps:// 開頭");
        if (Cameras.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"攝影機 ID {id} 已存在");
        var camera = new Camera
        {
            Id = id,
            Name = name,
            Rtsp = rtsp,
            SourceType = sourceType,
            ImagingType = imagingType,
            StreamType = streamType,
            SortOrder = Cameras.Count == 0 ? 1 : Cameras.Max(item => item.SortOrder) + 1
        };
        await _database.AddCameraAsync(camera);
        Track(camera);
        _suppressAutomaticDisplay = true;
        try
        {
            Cameras.Add(camera);
        }
        finally
        {
            _suppressAutomaticDisplay = false;
        }
        SelectedCamera = camera;
        _modbus?.AddCamera(camera);
        _recording?.AddCamera(camera);
        if (_recording?.Records(camera) == true)
        {
            await camera.ReplacePlaybackAsync(
                () => CreatePlayer(camera),
                () => CreateMedia(camera));
            await camera.StartPlaybackAsync();
        }
    }

    public async Task RemoveCameraAsync(Camera camera)
    {
        if (!Cameras.Contains(camera)) return;
        _removedCameras[camera] = 0;
        try
        {
            await _database.DeleteCameraAsync(camera.Id);
            if (_recording is not null) await _recording.RemoveCameraAsync(camera);
            _modbus?.RemoveCamera(camera);
            await _playbackRefreshGate.WaitAsync();
            try
            {
                await camera.ReleasePlaybackAsync();
                _authenticationErrors.TryRemove(camera, out _);
                if (ReferenceEquals(MaximizedCamera, camera)) MaximizedCamera = null;
                foreach (var slot in DisplayedCameraSlots.Where(slot => ReferenceEquals(slot.Camera, camera)))
                    slot.Camera = null;
                _eventActions.Resolve(camera);
                UpdateEventList(camera, false);
                Cameras.Remove(camera);
                _lastEventStatuses.TryRemove(camera, out _);
                SelectedCamera = Cameras.FirstOrDefault();
                camera.Dispose();
            }
            finally
            {
                _playbackRefreshGate.Release();
            }
        }
        catch
        {
            _removedCameras.TryRemove(camera, out _);
            throw;
        }
    }

    public async Task UpdateCameraAsync(Camera camera, string name, string rtsp,
        string sourceType, string imagingType, string streamType)
    {
        name = name.Trim();
        rtsp = rtsp.Trim();
        if (name.Length == 0 || rtsp.Length == 0) throw new ArgumentException("名稱與串流資料不可空白");
        if (!Uri.TryCreate(rtsp, UriKind.Absolute, out var uri) || uri.Scheme is not ("rtsp" or "rtsps"))
            throw new ArgumentException("網址必須以 rtsp:// 或 rtsps:// 開頭");

        camera.Name = name;
        camera.Rtsp = rtsp;
        camera.SourceType = sourceType;
        camera.ImagingType = imagingType;
        camera.StreamType = streamType;
        await _database.UpdateCameraAsync(camera);
        if (_modbus is not null)
        {
            var modbusSettings = NormalizeModbusSettings(await _database.LoadModbusSettingsAsync());
            await _database.SaveModbusSettingsAsync(modbusSettings);
            await _modbus.ApplyAsync(modbusSettings);
        }
        if (_recording is not null) await _recording.RefreshCameraAsync(camera);
        if (camera.Media is not null || _recording?.Records(camera) == true)
        {
            await camera.ReplacePlaybackAsync(
                () => CreatePlayer(camera),
                () => CreateMedia(camera));
            await camera.StartPlaybackAsync();
        }
    }

    public async Task MoveAsync(Camera source, Camera target)
    {
        var oldIndex = Cameras.IndexOf(source); var newIndex = Cameras.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;
        Cameras.Move(oldIndex, newIndex);
        await _database.SaveOrderAsync(Cameras);
    }

    public async Task ReplaceDisplayedCameraAsync(Camera source, int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= DisplayedCameraSlots.Count || ReferenceEquals(source, DisplayedCameraSlots[targetIndex].Camera)) return;

        var sourceSlot = DisplayedCameraSlots.FirstOrDefault(slot => ReferenceEquals(slot.Camera, source));
        if (sourceSlot is not null) sourceSlot.Camera = null;

        DisplayedCameraSlots[targetIndex].Camera = source;

        await _playbackRefreshGate.WaitAsync();
        try
        {
            if (source.Media is null)
            {
                await source.ReplacePlaybackAsync(
                    () => CreatePlayer(source),
                    () => CreateMedia(source));
            }

            await source.StartPlaybackAsync();
        }
        finally
        {
            _playbackRefreshGate.Release();
        }
    }

    public async Task ReconnectDisplayedCamerasAsync()
    {
        await _playbackRefreshGate.WaitAsync();
        try
        {
            await Task.WhenAll(Cameras.Select(camera => camera.StopPlaybackAsync()));

            var visibleCameras = DisplayedCameraSlots.Select(slot => slot.Camera).OfType<Camera>().ToHashSet();
            var camerasToPlay = Cameras
                .Where(camera => visibleCameras.Contains(camera) || _recording?.Records(camera) == true)
                .ToArray();
            await Task.WhenAll(camerasToPlay.Select(camera => camera.ReplacePlaybackAsync(
                () => CreatePlayer(camera),
                () => CreateMedia(camera))));

            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await Task.WhenAll(camerasToPlay.Select(camera => camera.StartPlaybackAsync()));
        }
        finally
        {
            _playbackRefreshGate.Release();
        }
    }

    private MediaPlayer CreatePlayer(Camera camera)
    {
        var player = new MediaPlayer(_libVlc) { EnableHardwareDecoding = true };
        player.Opening += (_, _) => UpdateCameraStatus(camera, false, "連線中");
        player.EncounteredError += (_, _) =>
        {
            if (_authenticationErrors.ContainsKey(camera))
            {
                UpdateCameraStatus(camera, false, "登入失敗");
                return;
            }

            UpdateCameraStatus(camera, false, "連線失敗，5 秒後重試");
            ScheduleReconnect(camera);
        };
        player.Playing += (_, _) =>
        {
            _authenticationErrors.TryRemove(camera, out _);
            UpdateCameraStatus(camera, true, "正常");
        };
        return player;
    }

    private Media CreateMedia(Camera camera) =>
        _recording?.CreateMedia(camera) ?? new Media(_libVlc, new Uri(camera.Rtsp));

    private async Task RestartPlaybackForRecordingSettingsAsync()
    {
        await _playbackRefreshGate.WaitAsync();
        try
        {
            var visible = DisplayedCameraSlots.Select(slot => slot.Camera).OfType<Camera>().ToHashSet();
            var camerasToPlay = Cameras
                .Where(camera => visible.Contains(camera) || _recording?.Records(camera) == true)
                .ToArray();

            await Task.WhenAll(Cameras.Select(camera => camera.StopPlaybackAsync()));
            await Task.WhenAll(camerasToPlay.Select(camera => camera.ReplacePlaybackAsync(
                () => CreatePlayer(camera),
                () => CreateMedia(camera))));
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await Task.WhenAll(camerasToPlay.Select(camera => camera.StartPlaybackAsync()));
        }
        finally
        {
            _playbackRefreshGate.Release();
        }
    }

    private void LibVlcLog(object? sender, LogEventArgs e)
    {
        var message = e.Message;
        if (!message.Contains("401", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("authentication failed", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("access denied", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("cannot authenticate", StringComparison.OrdinalIgnoreCase)) return;

        var matched = false;
        foreach (var camera in Cameras)
        {
            if (!Uri.TryCreate(camera.Rtsp, UriKind.Absolute, out var uri) ||
                !message.Contains(uri.Host, StringComparison.OrdinalIgnoreCase)) continue;

            _authenticationErrors[camera] = 0;
            matched = true;
        }

        if (matched) return;
        foreach (var camera in Cameras.Where(camera => camera.Player?.State == VLCState.Opening))
            _authenticationErrors[camera] = 0;
    }

    private void ScheduleReconnect(Camera camera)
    {
        if (_shutdown.IsCancellationRequested || _authenticationErrors.ContainsKey(camera) || _removedCameras.ContainsKey(camera)) return;
        _reconnectTasks.GetOrAdd(camera, ReconnectLoopAsync);
    }

    private async Task ReconnectLoopAsync(Camera camera)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), _shutdown.Token);
            if (_authenticationErrors.ContainsKey(camera))
            {
                UpdateCameraStatus(camera, false, "登入失敗");
                return;
            }

            while (!_shutdown.IsCancellationRequested && !_authenticationErrors.ContainsKey(camera) && !_removedCameras.ContainsKey(camera))
            {
                UpdateCameraStatus(camera, false, "重新連線中");

                try
                {
                    await _playbackRefreshGate.WaitAsync(_shutdown.Token);
                    try
                    {
                        if (_removedCameras.ContainsKey(camera)) break;
                        await camera.ReplacePlaybackAsync(
                            () => CreatePlayer(camera),
                            () => CreateMedia(camera));
                        await camera.StartPlaybackAsync();
                    }
                    finally
                    {
                        _playbackRefreshGate.Release();
                    }
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    UpdateCameraStatus(camera, false, "連線失敗，5 秒後重試");
                }

                await Task.Delay(TimeSpan.FromSeconds(5), _shutdown.Token);
                if (_authenticationErrors.ContainsKey(camera))
                {
                    UpdateCameraStatus(camera, false, "登入失敗");
                    break;
                }
                if (camera.Player?.IsPlaying == true) break;
                UpdateCameraStatus(camera, false, "連線失敗，5 秒後重試");
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            _reconnectTasks.TryRemove(camera, out _);
        }
    }

    private static void UpdateCameraStatus(Camera camera, bool online, string status)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            camera.StreamOnline = online;
            camera.StreamStatus = status;
        });
    }

    private void Track(Camera camera)
    {
        _lastEventStatuses[camera] = camera.Status;
        camera.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Camera.StreamOnline)) OnPropertyChanged(nameof(OnlineCount));
            if (e.PropertyName == nameof(Camera.EventDisplayName))
            {
                Application.Current.Dispatcher.BeginInvoke(() => UpdateActiveEventDetails(camera));
                return;
            }
            if (e.PropertyName != nameof(Camera.Status)) return;
            var previous = _lastEventStatuses.GetValueOrDefault(camera, "正常");
            _lastEventStatuses[camera] = camera.Status;
            var wasTriggered = previous is "溫升" or "預警" or "警報";
            var isTriggered = camera.Status is "溫升" or "預警" or "警報";
            if (!wasTriggered && isTriggered) _eventActions.Execute(camera);
            else if (wasTriggered && !isTriggered) _eventActions.Resolve(camera);
            Application.Current.Dispatcher.BeginInvoke(() => UpdateEventList(camera, isTriggered));
        };
    }

    private void UpdateEventList(Camera camera, bool isTriggered)
    {
        if (isTriggered)
        {
            if (!_activeEvents.TryGetValue(camera, out var item))
            {
                item = new CameraEvent
                {
                    Camera = camera,
                    EventType = camera.EventDisplayName,
                    Temperature = camera.Temperature,
                    RegionText = camera.ThermalRegionSummary
                };
                _activeEvents[camera] = item;
                Events.Insert(0, item);
                while (Events.Count > 100) Events.RemoveAt(Events.Count - 1);
            }
            else
            {
                item.EventType = camera.EventDisplayName;
                item.Temperature = camera.Temperature;
                item.RegionText = camera.ThermalRegionSummary;
            }
            return;
        }

        if (_activeEvents.Remove(camera, out var activeEvent))
        {
            activeEvent.Temperature = camera.Temperature;
            activeEvent.RegionText = camera.ThermalRegionSummary;
            activeEvent.Active = false;
        }
    }

    private void UpdateActiveEventDetails(Camera camera)
    {
        if (!_activeEvents.TryGetValue(camera, out var item)) return;
        item.EventType = camera.EventDisplayName;
        item.Temperature = camera.Temperature;
        item.RegionText = camera.ThermalRegionSummary;
    }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; OnPropertyChanged(name); }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));

    public ValueTask DisposeAsync()
    {
        _disposeTask ??= DisposeCoreAsync();
        return new ValueTask(_disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        _clock.Stop();
        await _shutdown.CancelAsync();
        if (_recording is not null) await _recording.DisposeAsync();
        if (_modbus is not null) await _modbus.DisposeAsync();
        var reconnectTasks = _reconnectTasks.Values.ToArray();
        if (reconnectTasks.Length > 0) await Task.WhenAll(reconnectTasks);
        _libVlc.Log -= LibVlcLog;
        _eventActions.Dispose();

        await _playbackRefreshGate.WaitAsync();
        try
        {
            await Task.WhenAll(Cameras.Select(camera => camera.ReleasePlaybackAsync()));
            await RecordingService.DeleteTemporaryFilesAsync(_recordingOutputDirectory);
            foreach (var camera in Cameras) camera.Dispose();
            await Task.Run(_libVlc.Dispose);
        }
        finally
        {
            _playbackRefreshGate.Release();
            _playbackRefreshGate.Dispose();
            _shutdown.Dispose();
        }
    }
}
