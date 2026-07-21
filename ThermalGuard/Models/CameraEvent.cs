using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ThermalGuard.Models;

public sealed class CameraEvent : INotifyPropertyChanged
{
    private string _eventType = "";
    private float _temperature;
    private bool _active = true;
    private string _regionText = "";

    public required Camera Camera { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.Now;
    public string EventType { get => _eventType; set => Set(ref _eventType, value); }
    public float Temperature { get => _temperature; set => Set(ref _temperature, value); }
    public bool Active { get => _active; set { Set(ref _active, value); OnPropertyChanged(nameof(StateText)); } }
    public string RegionText { get => _regionText; set => Set(ref _regionText, value); }
    public string StateText => Active ? "觸發中" : "已結束";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
