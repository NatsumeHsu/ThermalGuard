using System.ComponentModel;
using System.Runtime.CompilerServices;
using LibVLCSharp.Shared;
using System.Collections.ObjectModel;

namespace ThermalGuard.Models;

public sealed class Camera : INotifyPropertyChanged, IDisposable
{
    private readonly SemaphoreSlim _playbackGate = new(1, 1);
    private DateTime? _thresholdReachedAt;
    private readonly Queue<(DateTime At, float Temperature)> _temperatureHistory = [];
    private int _highTemperatureCycles;
    private int _riseCycles;
    private bool _highTemperatureConfirmed;
    private bool _riseConfirmed;
    private bool _suppressEventEvaluation;
    private readonly Dictionary<string, RegionAlarmSettings> _regionAlarmSettings = [];
    private readonly Dictionary<string, RegionAlarmState> _regionAlarmStates = [];

    public required string Id { get; init; }
    public required string Name { get; set => Set(ref field, value); }
    public required string Rtsp { get; set => Set(ref field, value); }
    public required string SourceType { get; set => Set(ref field, value); } = "RTSP 串流";
    public required string ImagingType { get; set => Set(ref field, value); } = "可見光";
    public required string StreamType { get; set => Set(ref field, value); } = "主碼流";
    public required int SortOrder { get; set; }
    public ObservableCollection<ThermalRegion> ThermalRegions { get; } = [];

    public MediaPlayer? Player { get; private set => Set(ref field, value); }

    public Media? Media { get; private set; }
    public float Temperature
    {
        get;
        set
        {
            Set(ref field, value);
            var now = DateTime.UtcNow;
            _temperatureHistory.Enqueue((now, value));
            UpdateConfirmationState(now);
            if (!_suppressEventEvaluation) EvaluateEventStatus(now);
        }
    }
    public string Status { get; set => Set(ref field, value); } = "讀取中";
    public bool Online { get; set => Set(ref field, value); }
    public string StreamStatus { get; set => Set(ref field, value); } = "等待連線";
    public bool StreamOnline { get; set => Set(ref field, value); }
    public bool EventEnabled { get; set { Set(ref field, value); EvaluateEventStatus(DateTime.UtcNow); } } = true;
    public double WarningTemperature { get; set { Set(ref field, value); EvaluateEventStatus(DateTime.UtcNow); } } = 50;
    public double AlarmTemperature { get; set { Set(ref field, value); EvaluateEventStatus(DateTime.UtcNow); } } = 70;
    public int TriggerSeconds { get; set { Set(ref field, value); EvaluateEventStatus(DateTime.UtcNow); } } = 3;
    public double TemperatureRise { get; set { Set(ref field, value); EvaluateEventStatus(DateTime.UtcNow); } } = 10;
    public int RiseWindowSeconds { get; set { Set(ref field, value); EvaluateEventStatus(DateTime.UtcNow); } } = 60;
    public int HighTemperatureConfirmCycle { get; set => Set(ref field, value); } = 3;
    public double HighTemperatureDeadband { get; set => Set(ref field, value); } = 2;
    public int RiseConfirmCycle { get; set => Set(ref field, value); } = 3;
    public double RiseDeadband { get; set => Set(ref field, value); } = 1;
    public bool ShowPopup { get; set => Set(ref field, value); } = true;
    public bool PlayAlarmSound { get; set => Set(ref field, value); } = true;
    public string AlarmSound { get; set => Set(ref field, value); } = "警報音 1";
    public bool TemperatureOnline { get; set => Set(ref field, value); }
    public string TemperatureStatus { get; set => Set(ref field, value); } = "未設定 Modbus";
    public bool ExternalAlarm { get; private set => Set(ref field, value); }
    public bool ExternalWarning { get; private set => Set(ref field, value); }
    public string ThermalRegionSummary { get; private set => Set(ref field, value); } = "尚無框溫資料";
    public string ThermalEventStatus
    {
        get;
        private set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new(nameof(ThermalEventStatus)));
            PropertyChanged?.Invoke(this, new(nameof(EventDisplayName)));
        }
    } = "正常";
    public string InternalEventStatus
    {
        get;
        private set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new(nameof(InternalEventStatus)));
            PropertyChanged?.Invoke(this, new(nameof(EventDisplayName)));
        }
    } = "正常";
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
    public int ThermalWarningRegionMask { get; private set; }
    public int ThermalAlarmRegionMask { get; private set; }
    public int InternalWarningRegionMask { get; private set; }
    public int InternalAlarmRegionMask { get; private set; }
    public int InternalRiseRegionMask { get; private set; }
    public IReadOnlyList<ActiveRegionAlarm> ActiveRegionAlarms { get; private set; } = [];

    public void UpdateThermalRegions(IReadOnlyList<float> temperatures, IReadOnlyList<int> eventCodes)
    {
        _suppressEventEvaluation = true;
        try
        {
            for (var index = 0; index < Math.Min(21, temperatures.Count); index++)
            {
                var region = ThermalRegions.FirstOrDefault(item => item.Number == index + 1);
                if (region is null)
                {
                    region = new ThermalRegion { Number = index + 1 };
                    var insertAt = ThermalRegions.TakeWhile(item => item.Number < region.Number).Count();
                    ThermalRegions.Insert(insertAt, region);
                }
                var historySeconds = _regionAlarmSettings.Values
                    .Where(item => item.RegionNumber == region.Number && item.EventType == "程式內部溫升")
                    .Select(item => item.RiseWindowSeconds)
                    .DefaultIfEmpty(RiseWindowSeconds)
                    .Max();
                region.RecordTemperature(temperatures[index], DateTime.UtcNow, historySeconds);
                region.EventCode = eventCodes[index];
            }
            Temperature = ThermalRegions.Count == 0 ? 0 : ThermalRegions.Max(region => region.Temperature);
            var alarmNumbers = ThermalRegions.Where(region => region.EventCode == 1 &&
                (ThermalAlarmRegionMask & (1 << (region.Number - 1))) != 0).Select(region => region.Number).ToArray();
            var warningNumbers = ThermalRegions.Where(region => region.EventCode == 2 &&
                (ThermalWarningRegionMask & (1 << (region.Number - 1))) != 0).Select(region => region.Number).ToArray();
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
        EvaluateEventStatus(DateTime.UtcNow, true);
    }

    public void ApplyRegionAlarmSettings(IEnumerable<RegionAlarmSettings> settings)
    {
        _regionAlarmSettings.Clear();
        foreach (var setting in settings.Where(item => item.CameraId.Equals(Id, StringComparison.OrdinalIgnoreCase)))
            _regionAlarmSettings[AlarmKey(setting.RegionNumber, setting.EventType)] = setting;
        _regionAlarmStates.Clear();
        ActiveRegionAlarms = [];
        PropertyChanged?.Invoke(this, new(nameof(ActiveRegionAlarms)));
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
        ThermalWarningRegionMask = settings.ThermalWarningRegionMask;
        ThermalAlarmRegionMask = settings.ThermalAlarmRegionMask;
        InternalWarningRegionMask = settings.InternalWarningRegionMask;
        InternalAlarmRegionMask = settings.InternalAlarmRegionMask;
        InternalRiseRegionMask = settings.InternalRiseRegionMask;
        _highTemperatureCycles = _riseCycles = 0;
        _highTemperatureConfirmed = _riseConfirmed = false;
        _thresholdReachedAt = null;
        EvaluateEventStatus(DateTime.UtcNow);
    }

    public void EvaluateEventStatus(DateTime now) => EvaluateEventStatus(now, false);

    private void EvaluateEventStatus(DateTime now, bool newSample)
    {
        if (_regionAlarmSettings.Count > 0)
        {
            EvaluateRegionAlarms(now, newSample);
            return;
        }
        ThermalEventStatus = EventEnabled
            ? ExternalAlarm ? "警報" : ExternalWarning ? "預警" : "正常"
            : "正常";
        var internalAlarm = ThermalRegions.Any(region =>
            (InternalAlarmRegionMask & (1 << (region.Number - 1))) != 0 && region.Temperature >= AlarmTemperature);
        var internalWarning = ThermalRegions.Any(region =>
            (InternalWarningRegionMask & (1 << (region.Number - 1))) != 0 && region.Temperature >= WarningTemperature);
        var internalRise = ThermalRegions.Any(region =>
            (InternalRiseRegionMask & (1 << (region.Number - 1))) != 0 && region.HasTemperatureRise(TemperatureRise));
        var rawInternalStatus = EventEnabled
            ? internalAlarm ? "警報" :
                internalWarning ? "預警" :
                internalRise ? "溫升" : "正常"
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

    private void EvaluateRegionAlarms(DateTime now, bool newSample)
    {
        var active = new List<ActiveRegionAlarm>();
        foreach (var setting in _regionAlarmSettings.Values)
        {
            var key = AlarmKey(setting.RegionNumber, setting.EventType);
            if (!_regionAlarmStates.TryGetValue(key, out var state))
                _regionAlarmStates[key] = state = new RegionAlarmState();
            var region = ThermalRegions.FirstOrDefault(item => item.Number == setting.RegionNumber);
            var condition = setting.Enabled && region is not null &&
                IsRegionAlarmCondition(region, setting, state.Active);

            if (!condition)
            {
                state.Cycles = 0;
                state.ReachedAt = null;
                state.Active = false;
                state.TimedOut = false;
                continue;
            }
            if (state.TimedOut) continue;

            if (!state.Active)
            {
                if (newSample) state.Cycles++;
                if (state.Cycles < Math.Max(1, setting.ConfirmCycles)) continue;
                state.Active = true;
                state.ActivatedAt = now;
            }

            if (setting.RetentionMode == "固定秒數" &&
                now - state.ActivatedAt >= TimeSpan.FromSeconds(Math.Max(1, setting.DurationSeconds)))
            {
                state.Active = false;
                state.TimedOut = true;
                continue;
            }
            active.Add(new ActiveRegionAlarm(key, setting.RegionNumber, setting.EventType,
                region!.Temperature, setting));
        }

        var previousKeys = ActiveRegionAlarms.Select(item => item.Key).Order().ToArray();
        var currentKeys = active.Select(item => item.Key).Order().ToArray();
        ActiveRegionAlarms = active;
        if (!previousKeys.SequenceEqual(currentKeys))
            PropertyChanged?.Invoke(this, new(nameof(ActiveRegionAlarms)));

        var thermal = active.Where(item => item.EventType.StartsWith("熱像儀", StringComparison.Ordinal)).ToArray();
        var internalEvents = active.Where(item => item.EventType.StartsWith("程式內部", StringComparison.Ordinal)).ToArray();
        ThermalEventStatus = HighestStatus(thermal.Select(item => item.EventType));
        InternalEventStatus = HighestStatus(internalEvents.Select(item => item.EventType));
        var regionNumbers = active.Select(item => item.RegionNumber).Distinct().Order().ToArray();
        ThermalRegionSummary = regionNumbers.Length == 0
            ? ThermalRegions.Count == 0 ? "尚無框溫資料" : $"最高溫框：{ThermalRegions.MaxBy(item => item.Temperature)!.Number}"
            : $"觸發框：{string.Join(", ", regionNumbers)}";
        Status = active.Any(item => item.EventType.EndsWith("警報", StringComparison.Ordinal)) ? "警報" :
            active.Any(item => item.EventType.EndsWith("預警", StringComparison.Ordinal)) ? "預警" :
            active.Any(item => item.EventType.EndsWith("溫升", StringComparison.Ordinal)) ? "溫升" : "正常";
    }

    private static bool IsRegionAlarmCondition(ThermalRegion region, RegionAlarmSettings setting, bool active)
    {
        var threshold = active ? setting.TriggerValue - setting.Deadband : setting.TriggerValue;
        return setting.EventType switch
        {
            "熱像儀預警" => region.EventCode == 2,
            "熱像儀警報" => region.EventCode == 1,
            "程式內部預警" or "程式內部警報" => region.Temperature >= threshold,
            "程式內部溫升" => region.HasTemperatureRise(threshold),
            _ => false
        };
    }

    private static string HighestStatus(IEnumerable<string> eventTypes) =>
        eventTypes.Any(item => item.EndsWith("警報", StringComparison.Ordinal)) ? "警報" :
        eventTypes.Any(item => item.EndsWith("預警", StringComparison.Ordinal)) ? "預警" :
        eventTypes.Any(item => item.EndsWith("溫升", StringComparison.Ordinal)) ? "溫升" : "正常";

    private static string AlarmKey(int regionNumber, string eventType) => $"{regionNumber}:{eventType}";

    private sealed class RegionAlarmState
    {
        public int Cycles { get; set; }
        public DateTime? ReachedAt { get; set; }
        public DateTime ActivatedAt { get; set; }
        public bool Active { get; set; }
        public bool TimedOut { get; set; }
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
        player?.Media = null;
        media?.Dispose();
        player?.Dispose();
    }
}
