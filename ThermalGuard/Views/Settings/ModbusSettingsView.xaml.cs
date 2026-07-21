using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ThermalGuard.Models;
using ThermalGuard.ViewModels;

namespace ThermalGuard.Views.Settings;

public partial class ModbusSettingsView : UserControl
{
    public IReadOnlyList<string> AccessModes { get; } = ["唯讀", "讀寫"];
    private readonly ObservableCollection<ModbusSettingRow> _rows = [];
    private bool _loaded;

    public ModbusSettingsView() => InitializeComponent();

    private async void UserControlLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded || DataContext is not MainViewModel viewModel) return;
        _loaded = true;
        var saved = (await viewModel.LoadModbusSettingsAsync()).ToDictionary(item => item.CameraId, StringComparer.OrdinalIgnoreCase);
        var thermalCameras = viewModel.Cameras.Where(camera => camera.SourceType == "熱像儀")
            .GroupBy(camera => new Uri(camera.Rtsp).Host, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        for (var index = 0; index < thermalCameras.Length; index++)
        {
            var camera = thermalCameras[index];
            var host = new Uri(camera.Rtsp).Host;
            saved.TryGetValue(camera.Id, out var setting);
            setting ??= saved.Values.FirstOrDefault(item => item.Host.Equals(host, StringComparison.OrdinalIgnoreCase));
            _rows.Add(new ModbusSettingRow
            {
                CameraId = camera.Id,
                DisplayName = $"{camera.Name}  ({host})",
                Enabled = setting?.Enabled ?? false,
                Host = host,
                Port = setting?.Port ?? 502,
                SlaveId = 1,
                RegisterAddress = 0,
                AlarmRegisterAddress = 4,
                AccessMode = setting?.AccessMode ?? AccessModes[0],
                Regions = new ObservableCollection<RegionSelection>(Enumerable.Range(1, 21)
                    .Select(number => new RegionSelection
                    {
                        Number = number,
                        Enabled = ((setting?.EnabledRegionMask ?? 0) & (1 << (number - 1))) != 0
                    })),
                PollIntervalMs = setting?.PollIntervalMs ?? 1000
            });
        }
        CameraSelector.ItemsSource = _rows;
        CameraSelector.SelectedIndex = _rows.Count > 0 ? 0 : -1;
    }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        try
        {
            foreach (var row in _rows)
                if (string.IsNullOrWhiteSpace(row.Host) || row.Port is < 1 or > 65535 ||
                    row.PollIntervalMs is < 100 or > 60000)
                    throw new ArgumentException($"{row.DisplayName}：請確認主機、Port 及輪詢間隔（100–60000 ms）");

            await viewModel.SaveModbusSettingsAsync(_rows.Select(row => new ModbusSettings(
                row.CameraId, row.Enabled, row.Host.Trim(), row.Port, 1,
                row.RegisterAddress, row.AlarmRegisterAddress, row.AccessMode,
                row.Regions.Where(region => region.Enabled).Aggregate(0, (mask, region) => mask | (1 << (region.Number - 1))),
                row.PollIntervalMs)));
            MessageBox.Show(Window.GetWindow(this), "Modbus 設定已儲存並開始套用", "Modbus 溫度讀取",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "儲存失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private sealed class ModbusSettingRow
    {
        public required string CameraId { get; init; }
        public required string DisplayName { get; init; }
        public bool Enabled { get; set; }
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public byte SlaveId { get; set; }
        public ushort RegisterAddress { get; set; }
        public ushort AlarmRegisterAddress { get; set; }
        public string AccessMode { get; set; } = "唯讀";
        public ObservableCollection<RegionSelection> Regions { get; set; } = [];
        public int PollIntervalMs { get; set; }
    }

    private sealed class RegionSelection
    {
        public required int Number { get; init; }
        public string Label => $"溫度{Number}";
        public bool Enabled { get; set; }
    }
}
