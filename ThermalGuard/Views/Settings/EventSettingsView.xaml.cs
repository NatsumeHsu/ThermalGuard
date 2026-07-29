using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ThermalGuard.Models;
using ThermalGuard.Services;
using ThermalGuard.ViewModels;

namespace ThermalGuard.Views.Settings;

public partial class EventSettingsView : UserControl
{
    private static readonly string[] EventTypes =
        ["程式內部預警", "程式內部警報", "程式內部溫升", "熱像儀預警", "熱像儀警報"];
    public IReadOnlyList<string> AlarmSounds { get; } = EventActionService.AlarmSounds;
    public IReadOnlyList<string> RetentionModes { get; } = ["直到確認或事件結束", "固定秒數"];
    private readonly ObservableCollection<CameraChoice> _cameras = [];
    private readonly ObservableCollection<int> _regions = new(Enumerable.Range(1, 21));
    private readonly List<RegionAlarmRow> _rows = [];
    private bool _loaded;

    public EventSettingsView() => InitializeComponent();

    private async void UserControlLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded || DataContext is not MainViewModel viewModel) return;
        _loaded = true;
        var saved = (await viewModel.LoadRegionAlarmSettingsAsync())
            .ToDictionary(item => (item.CameraId, item.RegionNumber, item.EventType));
        foreach (var camera in viewModel.EventSettingCameras)
        {
            _cameras.Add(new CameraChoice(camera.Id, $"{camera.Id}  {camera.Name}", camera));
            for (var region = 1; region <= 21; region++)
            foreach (var eventType in EventTypes)
            {
                saved.TryGetValue((camera.Id, region, eventType), out var setting);
                _rows.Add(setting is null ? CreateDefault(camera, region, eventType) : new RegionAlarmRow(setting));
            }
        }
        CameraSelector.ItemsSource = _cameras;
        RegionSelector.ItemsSource = _regions;
        CameraSelector.SelectedIndex = _cameras.Count > 0 ? 0 : -1;
        RegionSelector.SelectedIndex = 0;
        RefreshRows();
    }

    private static RegionAlarmRow CreateDefault(Camera camera, int region, string eventType)
    {
        var bit = 1 << (region - 1);
        var enabled = eventType switch
        {
            "程式內部預警" => (camera.InternalWarningRegionMask & bit) != 0,
            "程式內部警報" => (camera.InternalAlarmRegionMask & bit) != 0,
            "程式內部溫升" => (camera.InternalRiseRegionMask & bit) != 0,
            "熱像儀預警" => (camera.ThermalWarningRegionMask & bit) != 0,
            "熱像儀警報" => (camera.ThermalAlarmRegionMask & bit) != 0,
            _ => false
        };
        var triggerValue = eventType switch
        {
            "程式內部預警" => camera.WarningTemperature,
            "程式內部警報" => camera.AlarmTemperature,
            "程式內部溫升" => camera.TemperatureRise,
            _ => 0
        };
        var isRise = eventType == "程式內部溫升";
        var isInternal = eventType.StartsWith("程式內部", StringComparison.Ordinal);
        return new RegionAlarmRow(new RegionAlarmSettings(camera.Id, region, eventType, enabled,
            triggerValue, isInternal ? camera.TriggerSeconds : 0,
            isInternal ? isRise ? camera.RiseConfirmCycle : camera.HighTemperatureConfirmCycle : 1,
            isInternal ? isRise ? camera.RiseDeadband : camera.HighTemperatureDeadband : 0,
            isRise ? camera.RiseWindowSeconds : 60, camera.ShowPopup, camera.PlayAlarmSound,
            camera.AlarmSound, "直到確認或事件結束", 10));
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshRows();

    private void RefreshRows()
    {
        if (CameraSelector.SelectedItem is not CameraChoice camera ||
            RegionSelector.SelectedItem is not int region) return;
        AlarmGrid.ItemsSource = _rows.Where(item => item.CameraId == camera.Id &&
            item.RegionNumber == region).ToArray();
    }

    private async void SaveEventsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        try
        {
            foreach (var row in _rows)
            {
                if (row.ConfirmCycles is < 1 or > 1000 ||
                    row.Deadband is < 0 or > 1000 || row.RiseWindowSeconds is < 1 or > 86400 ||
                    row.DurationSeconds is < 1 or > 86400)
                    throw new ArgumentException($"{row.CameraId} 框溫 {row.RegionNumber} {row.EventType}：觸發或事件參數超出範圍");
                if (row.EventType.StartsWith("程式內部", StringComparison.Ordinal) && row.TriggerValue <= -273.2)
                    throw new ArgumentException($"{row.CameraId} 框溫 {row.RegionNumber} {row.EventType}：觸發值不正確");
            }
            await viewModel.SaveRegionAlarmSettingsAsync(_rows.Select(item => item.ToSettings()));
            MessageBox.Show(Window.GetWindow(this), "每個框溫的五種警報設定已儲存並套用", "事件設定",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "儲存失敗",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private sealed record CameraChoice(string Id, string DisplayName, Camera Camera);

    private sealed class RegionAlarmRow
    {
        public RegionAlarmRow(RegionAlarmSettings value)
        {
            CameraId = value.CameraId;
            RegionNumber = value.RegionNumber;
            EventType = value.EventType;
            Enabled = value.Enabled;
            TriggerValue = value.TriggerValue;
            TriggerSeconds = value.TriggerSeconds;
            ConfirmCycles = value.ConfirmCycles;
            Deadband = value.Deadband;
            RiseWindowSeconds = value.RiseWindowSeconds;
            ShowPopup = value.ShowPopup;
            PlayAlarmSound = value.PlayAlarmSound;
            AlarmSound = value.AlarmSound;
            RetentionMode = value.RetentionMode;
            DurationSeconds = value.DurationSeconds;
        }
        public string CameraId { get; }
        public int RegionNumber { get; }
        public string EventType { get; }
        public bool IsInternal => EventType.StartsWith("程式內部", StringComparison.Ordinal);
        public bool IsRise => EventType == "程式內部溫升";
        public bool Enabled { get; set; }
        public double TriggerValue { get; set; }
        public int TriggerSeconds { get; set; }
        public int ConfirmCycles { get; set; }
        public double Deadband { get; set; }
        public int RiseWindowSeconds { get; set; }
        public bool ShowPopup { get; set; }
        public bool PlayAlarmSound { get; set; }
        public string AlarmSound { get; set; }
        public string RetentionMode { get; set; }
        public int DurationSeconds { get; set; }
        public double? TriggerValueDisplay
        {
            get => IsInternal ? TriggerValue : null;
            set { if (IsInternal && value.HasValue) TriggerValue = value.Value; }
        }
        public int? ConfirmCyclesDisplay
        {
            get => IsInternal ? ConfirmCycles : null;
            set { if (IsInternal && value.HasValue) ConfirmCycles = value.Value; }
        }
        public double? DeadbandDisplay
        {
            get => IsInternal ? Deadband : null;
            set { if (IsInternal && value.HasValue) Deadband = value.Value; }
        }
        public int? RiseWindowDisplay
        {
            get => IsRise ? RiseWindowSeconds : null;
            set { if (IsRise && value.HasValue) RiseWindowSeconds = value.Value; }
        }
        public RegionAlarmSettings ToSettings() => new(CameraId, RegionNumber, EventType, Enabled,
            TriggerValue, TriggerSeconds, ConfirmCycles, Deadband, RiseWindowSeconds, ShowPopup,
            PlayAlarmSound, AlarmSound, RetentionMode, DurationSeconds);
    }
}
