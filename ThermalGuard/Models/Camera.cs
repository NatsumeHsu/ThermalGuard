using System.ComponentModel;
using System.Runtime.CompilerServices;
using LibVLCSharp.Shared;
using System.Collections.ObjectModel;

namespace ThermalGuard.Models;

public sealed class Camera : INotifyPropertyChanged, IDisposable
{
    private readonly SemaphoreSlim _playbackGate = new(1, 1);
    private float _temperature;
    private string _status = "讀取中";
    private bool _online;
    private string _streamStatus = "等待連線";
    private bool _streamOnline;
    private string _name = "";
    private string _rtsp = "";
    private string _sourceType = "RTSP 串流";
    private string _imagingType = "可見光";
    private string _streamType = "主碼流";
    private bool _eventEnabled = true;
    private double _warningTemperature = 50;
    private double _alarmTemperature = 70;
    private int _triggerSeconds = 3;
    private DateTime? _thresholdReachedAt;
    private double _temperatureRise = 10;
    private int _riseWindowSeconds = 60;
    private readonly Queue<(DateTime At, float Temperature)> _temperatureHistory = [];
    private int _highTemperatureCycles;
    private int _riseCycles;
    private bool _highTemperatureConfirmed;
    private bool _riseConfirmed;
    private int _highTemperatureConfirmCycle = 3;
    private double _highTemperatureDeadband = 2;
    private int _riseConfirmCycle = 3;
    private double _riseDeadband = 1;
    private bool _showPopup = true;
    private bool _playAlarmSound = true;
    private string _alarmSound = "警報音 1";
    private bool _temperatureOnline;
    private string _temperatureStatus = "未設定 Modbus";
    private bool _externalAlarm;
    private bool _externalWarning;
    private bool _suppressEventEvaluation;
    private int _thermalWarningRegionMask;
    private int _thermalAlarmRegionMask;
    private bool _internalWarningEnabled = true;
    private bool _internalAlarmEnabled = true;
    private bool _internalRiseEnabled = true;
    private int _internalWarningRegionMask;
    private int _internalAlarmRegionMask;
    private int _internalRiseRegionMask;
    private string _thermalRegionSummary = "尚無框溫資料";
    private string _thermalEventStatus = "正常";
    private string _internalEventStatus = "正常";

    public required string Id { get; init; }
    public required string Name { get => _name; set => Set(ref _name, value); }
    public required string Rtsp { get => _rtsp; set => Set(ref _rtsp, value); }
    public required string SourceType { get => _sourceType; set => Set(ref _sourceType, value); }
    public required string ImagingType { get => _imagingType; set => Set(ref _imagingType, value); }
    public required string StreamType { get => _streamType; set => Set(ref _streamType, value); }
    public required int SortOrder { get; set; }
    public ObservableCollection<ThermalRegion> ThermalRegions { get; } = [];
    private MediaPlayer? _player;
    private Media? _media;

    public MediaPlayer? Player
    {
        get => _player;
        private set => Set(ref _player, value);
    }

    public Media? Media
    {
        get => _media;
        private set => _media = value;
    }
    public float Temperature
    {
        get => _temperature;
        set
        {
            Set(ref _temperature, value);
            var now = DateTime.UtcNow;
            _temperatureHistory.Enqueue((now, value));
            UpdateConfirmationState(now);
            if (!_suppressEventEvaluation) EvaluateEventStatus(now);
        }
    }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool Online { get => _online; set => Set(ref _online, value); }
    public string StreamStatus { get => _streamStatus; set => Set(ref _streamStatus, value); }
    public bool StreamOnline { get => _streamOnline; set => Set(ref _streamOnline, value); }
    public bool EventEnabled { get => _eventEnabled; set { Set(ref _eventEnabled, value); EvaluateEventStatus(DateTime.UtcNow); } }
    public double WarningTemperature { get => _warningTemperature; set { Set(ref _warningTemperature, value); EvaluateEventStatus(DateTime.UtcNow); } }
    public double AlarmTemperature { get => _alarmTemperature; set { Set(ref _alarmTemperature, value); EvaluateEventStatus(DateTime.UtcNow); } }
    public int TriggerSeconds { get => _triggerSeconds; set { Set(ref _triggerSeconds, value); EvaluateEventStatus(DateTime.UtcNow); } }
    public double TemperatureRise { get => _temperatureRise; set { Set(ref _temperatureRise, value); EvaluateEventStatus(DateTime.UtcNow); } }
    public int RiseWindowSeconds { get => _riseWindowSeconds; set { Set(ref _riseWindowSeconds, value); EvaluateEventStatus(DateTime.UtcNow); } }
    public int HighTemperatureConfirmCycle { get => _highTemperatureConfirmCycle; set => Set(ref _highTemperatureConfirmCycle, value); }
    public double HighTemperatureDeadband { get => _highTemperatureDeadband; set => Set(ref _highTemperatureDeadband, value); }
    public int RiseConfirmCycle { get => _riseConfirmCycle; set => Set(ref _riseConfirmCycle, value); }
    public double RiseDeadband { get => _riseDeadband; set => Set(ref _riseDeadband, value); }
    public bool ShowPopup { get => _showPopup; set => Set(ref _showPopup, value); }
    public bool PlayAlarmSound { get => _playAlarmSound; set => Set(ref _playAlarmSound, value); }
    public string AlarmSound { get => _alarmSound; set => Set(ref _alarmSound, value); }
    public bool TemperatureOnline { get => _temperatureOnline; set => Set(ref _temperatureOnline, value); }
    public string TemperatureStatus { get => _temperatureStatus; set => Set(ref _temperatureStatus, value); }
    public bool ExternalAlarm { get => _externalAlarm; private set => Set(ref _externalAlarm, value); }
    public bool ExternalWarning { get => _externalWarning; private set => Set(ref _externalWarning, value); }
    public string ThermalRegionSummary { get => _thermalRegionSummary; private set => Set(ref _thermalRegionSummary, value); }
    public string ThermalEventStatus
    {
        get => _thermalEventStatus;
        private set
        {
            if (_thermalEventStatus == value) return;
            _thermalEventStatus = value;
            PropertyChanged?.Invoke(this, new(nameof(ThermalEventStatus)));
            PropertyChanged?.Invoke(this, new(nameof(EventDisplayName)));
        }
    }
    public string InternalEventStatus
    {
        get => _internalEventStatus;
        private set
        {
            if (_internalEventStatus == value) return;
            _internalEventStatus = value;
            PropertyChanged?.Invoke(this, new(nameof(InternalEventStatus)));
            PropertyChanged?.Invoke(this, new(nameof(EventDisplayName)));
        }
    }
    public string EventDisplayName
    {
        get
        {
            var names = new List<string>(2);
            if (ThermalEventStatus != "正常") names.Add($"熱像儀{ThermalEventStatus}");
            if (InternalEventStatus != "正常") names.Add($"程式內部{InternalEventStatus}");
            return names.Count == 0 ? "正常" : string.Join("、", names);
        }
    }
    public int ThermalWarningRegionMask => _thermalWarningRegionMask;
    public int ThermalAlarmRegionMask => _thermalAlarmRegionMask;
    public bool InternalWarningEnabled => _internalWarningEnabled;
    public bool InternalAlarmEnabled => _internalAlarmEnabled;
    public bool InternalRiseEnabled => _internalRiseEnabled;
    public int InternalWarningRegionMask => _internalWarningRegionMask;
    public int InternalAlarmRegionMask => _internalAlarmRegionMask;
    public int InternalRiseRegionMask => _internalRiseRegionMask;

    public void UpdateThermalRegions(IReadOnlyList<float> temperatures, IReadOnlyList<int> eventCodes,
        int enabledRegionMask)
    {
        _suppressEventEvaluation = true;
        try
        {
        for (var index = ThermalRegions.Count - 1; index >= 0; index--)
            if ((enabledRegionMask & (1 << (ThermalRegions[index].Number - 1))) == 0)
                ThermalRegions.RemoveAt(index);
        for (var index = 0; index < Math.Min(21, temperatures.Count); index++)
        {
            if ((enabledRegionMask & (1 << index)) == 0) continue;
            var region = ThermalRegions.FirstOrDefault(item => item.Number == index + 1);
            if (region is null)
            {
                region = new ThermalRegion { Number = index + 1 };
                var insertAt = ThermalRegions.TakeWhile(item => item.Number < region.Number).Count();
                ThermalRegions.Insert(insertAt, region);
            }
            region.RecordTemperature(temperatures[index], DateTime.UtcNow, RiseWindowSeconds);
            region.EventCode = eventCodes[index];
        }
        Temperature = ThermalRegions.Count == 0 ? 0 : ThermalRegions.Max(region => region.Temperature);
        var alarmNumbers = ThermalRegions.Where(region => region.EventCode == 1 &&
            (_thermalAlarmRegionMask & (1 << (region.Number - 1))) != 0).Select(region => region.Number).ToArray();
        var warningNumbers = ThermalRegions.Where(region => region.EventCode == 2 &&
            (_thermalWarningRegionMask & (1 << (region.Number - 1))) != 0).Select(region => region.Number).ToArray();
        var hottest = ThermalRegions.OrderByDescending(region => region.Temperature).FirstOrDefault();
        ThermalRegionSummary = alarmNumbers.Length > 0 && warningNumbers.Length > 0
            ? $"警報框：{string.Join(", ", alarmNumbers)} · 預警框：{string.Join(", ", warningNumbers)}"
            : alarmNumbers.Length > 0 ? $"警報框：{string.Join(", ", alarmNumbers)}"
            : warningNumbers.Length > 0 ? $"預警框：{string.Join(", ", warningNumbers)}"
            : hottest is null ? "尚無框溫資料" : $"最高溫框：{hottest.Number}";
        ExternalAlarm = alarmNumbers.Length > 0;
        ExternalWarning = warningNumbers.Length > 0;
        TemperatureOnline = true;
        TemperatureStatus = $"Modbus 正常 · {ThermalRegions.Count} 組";
        }
        finally
        {
            _suppressEventEvaluation = false;
        }
        EvaluateEventStatus(DateTime.UtcNow);
    }

    public void ApplyEventSettings(EventSettings settings)
    {
        EventEnabled = settings.Enabled;
        WarningTemperature = settings.WarningTemperature;
        AlarmTemperature = settings.AlarmTemperature;
        TriggerSeconds = settings.TriggerSeconds;
        TemperatureRise = settings.TemperatureRise;
        RiseWindowSeconds = settings.RiseWindowSeconds;
        HighTemperatureConfirmCycle = settings.HighTemperatureConfirmCycle;
        HighTemperatureDeadband = settings.HighTemperatureDeadband;
        RiseConfirmCycle = settings.RiseConfirmCycle;
        RiseDeadband = settings.RiseDeadband;
        ShowPopup = settings.ShowPopup;
        PlayAlarmSound = settings.PlayAlarmSound;
        AlarmSound = settings.AlarmSound;
        _thermalWarningRegionMask = settings.ThermalWarningRegionMask;
        _thermalAlarmRegionMask = settings.ThermalAlarmRegionMask;
        _internalWarningEnabled = settings.InternalWarningEnabled;
        _internalAlarmEnabled = settings.InternalAlarmEnabled;
        _internalRiseEnabled = settings.InternalRiseEnabled;
        _internalWarningRegionMask = settings.InternalWarningRegionMask;
        _internalAlarmRegionMask = settings.InternalAlarmRegionMask;
        _internalRiseRegionMask = settings.InternalRiseRegionMask;
        _highTemperatureCycles = _riseCycles = 0;
        _highTemperatureConfirmed = _riseConfirmed = false;
        _thresholdReachedAt = null;
        EvaluateEventStatus(DateTime.UtcNow);
    }

    public void EvaluateEventStatus(DateTime now)
    {
        ThermalEventStatus = ExternalAlarm ? "警報" : ExternalWarning ? "預警" : "正常";
        var internalAlarm = ThermalRegions.Any(region =>
            (_internalAlarmRegionMask & (1 << (region.Number - 1))) != 0 && region.Temperature >= AlarmTemperature);
        var internalWarning = ThermalRegions.Any(region =>
            (_internalWarningRegionMask & (1 << (region.Number - 1))) != 0 && region.Temperature >= WarningTemperature);
        var internalRise = ThermalRegions.Any(region =>
            (_internalRiseRegionMask & (1 << (region.Number - 1))) != 0 && region.HasTemperatureRise(TemperatureRise));
        var rawInternalStatus = EventEnabled
            ? internalAlarm && _internalAlarmEnabled ? "警報" :
                internalWarning && _internalWarningEnabled ? "預警" :
                internalRise && _internalRiseEnabled ? "溫升" : "正常"
            : "正常";

        if (rawInternalStatus == "正常")
        {
            _thresholdReachedAt = null;
            InternalEventStatus = "正常";
        }
        else
        {
            _thresholdReachedAt ??= now;
            InternalEventStatus = now - _thresholdReachedAt >= TimeSpan.FromSeconds(TriggerSeconds)
                ? rawInternalStatus
                : "正常";
        }

        Status = ThermalEventStatus == "警報" || InternalEventStatus == "警報" ? "警報" :
            ThermalEventStatus == "預警" || InternalEventStatus == "預警" ? "預警" :
            InternalEventStatus == "溫升" ? "溫升" : "正常";
    }

    private void UpdateConfirmationState(DateTime now)
    {
        var cutoff = now - TimeSpan.FromSeconds(RiseWindowSeconds);
        while (_temperatureHistory.Count > 0 && _temperatureHistory.Peek().At < cutoff)
            _temperatureHistory.Dequeue();

        if (_highTemperatureConfirmed)
        {
            if (Temperature < WarningTemperature - HighTemperatureDeadband)
            {
                _highTemperatureConfirmed = false;
                _highTemperatureCycles = 0;
            }
        }
        else
        {
            _highTemperatureCycles = Temperature >= WarningTemperature ? _highTemperatureCycles + 1 : 0;
            _highTemperatureConfirmed = _highTemperatureCycles >= HighTemperatureConfirmCycle;
        }

        var rise = _temperatureHistory.Count == 0 ? 0 :
            Temperature - _temperatureHistory.Min(sample => sample.Temperature);
        if (_riseConfirmed)
        {
            if (rise < TemperatureRise - RiseDeadband)
            {
                _riseConfirmed = false;
                _riseCycles = 0;
            }
        }
        else
        {
            _riseCycles = rise >= TemperatureRise ? _riseCycles + 1 : 0;
            _riseConfirmed = _riseCycles >= RiseConfirmCycle;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public async Task StartPlaybackAsync()
    {
        await _playbackGate.WaitAsync();
        try
        {
            if (Player is not null && Media is not null && !Player.IsPlaying)
                await Task.Run(() => Player.Play());
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    public async Task ReleasePlaybackAsync()
    {
        await _playbackGate.WaitAsync();
        try
        {
            await ReleasePlaybackCoreAsync();
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    public async Task StopPlaybackAsync()
    {
        await _playbackGate.WaitAsync();
        try
        {
            await ReleaseMediaCoreAsync();
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    public async Task ReplacePlaybackAsync(Func<MediaPlayer> createPlayer, Func<Media> createMedia)
    {
        await _playbackGate.WaitAsync();
        try
        {
            await ReleaseMediaCoreAsync();
            var player = Player ?? createPlayer();
            var media = createMedia();
            Player = player;
            Media = media;
            await Task.Run(() => player.Media = media);
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    private async Task ReleaseMediaCoreAsync()
    {
        var player = Player;
        var media = Media;
        Media = null;

        if (player is not null)
        {
            await StopAsync(player);
            await Task.Run(() => player.Media = null);
        }

        if (media is not null) await Task.Run(media.Dispose);
    }

    private async Task ReleasePlaybackCoreAsync()
    {
        var player = Player;
        var media = Media;

        Player = null;
        Media = null;

        if (player is null)
        {
            media?.Dispose();
            return;
        }

        if (player.State is not (VLCState.Stopped or VLCState.Ended or VLCState.Error or VLCState.NothingSpecial))
        {
            await StopAsync(player);
        }

        await Task.Run(() =>
        {
            player.Media = null;
            media?.Dispose();
            player.Dispose();
        });
    }

    private static async Task StopAsync(MediaPlayer player)
    {
        if (player.State is VLCState.Stopped or VLCState.Ended or VLCState.Error or VLCState.NothingSpecial) return;

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStopped(object? sender, EventArgs args) => stopped.TrySetResult();
        player.Stopped += OnStopped;
        try
        {
            await Task.Run(player.Stop);
            await Task.WhenAny(stopped.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            player.Stopped -= OnStopped;
        }
    }

    public void Dispose()
    {
        var player = Player;
        var media = Media;
        Player = null;
        Media = null;

        player?.Stop();
        if (player is not null) player.Media = null;
        media?.Dispose();
        player?.Dispose();
    }
}
