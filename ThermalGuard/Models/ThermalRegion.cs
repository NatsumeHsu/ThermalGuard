using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ThermalGuard.Models;

public sealed class ThermalRegion : INotifyPropertyChanged
{
    private float _temperature;
    private int _eventCode;
    private readonly Queue<(DateTime At, float Temperature)> _history = [];

    public int Number { get; init; }
    public ushort TemperatureRegister => (ushort)((Number - 1) * 5);
    public ushort AlarmRegister => (ushort)(TemperatureRegister + 4);
    public float Temperature { get => _temperature; set => Set(ref _temperature, value); }
    public int EventCode
    {
        get => _eventCode;
        set
        {
            if (_eventCode == value) return;
            _eventCode = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EventCode)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        }
    }
    public string StatusText => EventCode switch { 1 => "警報", 2 => "預警", _ => "正常" };

    public void RecordTemperature(float temperature, DateTime now, int windowSeconds)
    {
        Temperature = temperature;
        _history.Enqueue((now, temperature));
        var cutoff = now - TimeSpan.FromSeconds(windowSeconds);
        while (_history.Count > 0 && _history.Peek().At < cutoff) _history.Dequeue();
    }

    public bool HasTemperatureRise(double threshold) => _history.Count > 0 &&
        Temperature - _history.Min(item => item.Temperature) >= threshold;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
