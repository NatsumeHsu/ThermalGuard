using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ThermalGuard.Models;
using ThermalGuard.Services;
using ThermalGuard.ViewModels;

namespace ThermalGuard.Views.Settings;

public partial class EventSettingsView : UserControl
{
    public IReadOnlyList<string> AlarmSounds { get; } = EventActionService.AlarmSounds;
    private readonly ObservableCollection<EventSettingRow> _rows = [];
    private bool _loaded;

    public EventSettingsView() => InitializeComponent();

    private void UserControlLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded || DataContext is not MainViewModel viewModel) return;
        _loaded = true;
        foreach (var camera in viewModel.EventSettingCameras)
            _rows.Add(new EventSettingRow
            {
                CameraId = camera.Id,
                DisplayName = $"{camera.Id}  {camera.Name}",
                Enabled = camera.EventEnabled,
                WarningTemperature = camera.WarningTemperature,
                AlarmTemperature = camera.AlarmTemperature,
                TriggerSeconds = camera.TriggerSeconds,
                TemperatureRise = camera.TemperatureRise,
                RiseWindowSeconds = camera.RiseWindowSeconds,
                HighTemperatureConfirmCycle = camera.HighTemperatureConfirmCycle,
                HighTemperatureDeadband = camera.HighTemperatureDeadband,
                RiseConfirmCycle = camera.RiseConfirmCycle,
                RiseDeadband = camera.RiseDeadband,
                ShowPopup = camera.ShowPopup,
                PlayAlarmSound = camera.PlayAlarmSound,
                AlarmSound = camera.AlarmSound,
                ThermalRegions = new ObservableCollection<EventRegionRow>(Enumerable.Range(1, 21)
                    .Select(number => new EventRegionRow
                    {
                        Number = number,
                        InternalWarningEnabled = (camera.InternalWarningRegionMask & (1 << (number - 1))) != 0,
                        InternalAlarmEnabled = (camera.InternalAlarmRegionMask & (1 << (number - 1))) != 0,
                        InternalRiseEnabled = (camera.InternalRiseRegionMask & (1 << (number - 1))) != 0,
                        WarningEnabled = (camera.ThermalWarningRegionMask & (1 << (number - 1))) != 0,
                        AlarmEnabled = (camera.ThermalAlarmRegionMask & (1 << (number - 1))) != 0
                    })),
                InternalWarningEnabled = camera.InternalWarningEnabled,
                InternalAlarmEnabled = camera.InternalAlarmEnabled,
                InternalRiseEnabled = camera.InternalRiseEnabled
            });
        CameraSelector.ItemsSource = _rows;
        CameraSelector.SelectedIndex = _rows.Count > 0 ? 0 : -1;
    }

    private async void SaveEventsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        try
        {
            foreach (var row in _rows)
                if (row.WarningTemperature < -100 || row.AlarmTemperature <= row.WarningTemperature ||
                    row.TriggerSeconds is < 0 or > 3600 || row.TemperatureRise is <= 0 or > 1000 ||
                    row.RiseWindowSeconds is < 1 or > 86400 ||
                    row.HighTemperatureConfirmCycle is < 1 or > 1000 ||
                    row.RiseConfirmCycle is < 1 or > 1000 ||
                    row.HighTemperatureDeadband is < 0 or > 1000 || row.RiseDeadband is < 0 or > 1000)
                    throw new ArgumentException($"{row.DisplayName}：警報須高於預警；確認次數為 1–1000；Deadband 不可小於 0；溫升須大於 0；觀察秒數為 1–86400");

            await viewModel.SaveEventSettingsAsync(_rows.Select(row => new EventSettings(
                row.CameraId, row.Enabled, row.WarningTemperature, row.AlarmTemperature,
                row.TriggerSeconds, row.TemperatureRise, row.RiseWindowSeconds,
                row.HighTemperatureConfirmCycle, row.HighTemperatureDeadband,
                row.RiseConfirmCycle, row.RiseDeadband, row.ShowPopup, row.PlayAlarmSound,
                row.AlarmSound,
                row.ThermalRegions.Where(item => item.WarningEnabled).Aggregate(0, (mask, item) => mask | (1 << (item.Number - 1))),
                row.ThermalRegions.Where(item => item.AlarmEnabled).Aggregate(0, (mask, item) => mask | (1 << (item.Number - 1))),
                row.InternalWarningEnabled, row.InternalAlarmEnabled, row.InternalRiseEnabled,
                row.ThermalRegions.Where(item => item.InternalWarningEnabled).Aggregate(0, (mask, item) => mask | (1 << (item.Number - 1))),
                row.ThermalRegions.Where(item => item.InternalAlarmEnabled).Aggregate(0, (mask, item) => mask | (1 << (item.Number - 1))),
                row.ThermalRegions.Where(item => item.InternalRiseEnabled).Aggregate(0, (mask, item) => mask | (1 << (item.Number - 1))))));
            MessageBox.Show(Window.GetWindow(this), "事件設定已儲存並套用", "事件設定",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "儲存失敗",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private sealed class EventSettingRow
    {
        public required string CameraId { get; init; }
        public required string DisplayName { get; init; }
        public bool Enabled { get; set; }
        public double WarningTemperature { get; set; }
        public double AlarmTemperature { get; set; }
        public int TriggerSeconds { get; set; }
        public double TemperatureRise { get; set; }
        public int RiseWindowSeconds { get; set; }
        public int HighTemperatureConfirmCycle { get; set; }
        public double HighTemperatureDeadband { get; set; }
        public int RiseConfirmCycle { get; set; }
        public double RiseDeadband { get; set; }
        public bool ShowPopup { get; set; }
        public bool PlayAlarmSound { get; set; }
        public string AlarmSound { get; set; } = EventActionService.AlarmSounds[0];
        public ObservableCollection<EventRegionRow> ThermalRegions { get; set; } = [];
        public bool InternalWarningEnabled { get; set; }
        public bool InternalAlarmEnabled { get; set; }
        public bool InternalRiseEnabled { get; set; }
    }

    private sealed class EventRegionRow
    {
        public required int Number { get; init; }
        public string Label => $"溫度{Number}";
        public bool WarningEnabled { get; set; }
        public bool AlarmEnabled { get; set; }
        public bool InternalWarningEnabled { get; set; }
        public bool InternalAlarmEnabled { get; set; }
        public bool InternalRiseEnabled { get; set; }
    }
}
