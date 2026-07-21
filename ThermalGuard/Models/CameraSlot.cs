using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ThermalGuard.Models;

public sealed class CameraSlot : INotifyPropertyChanged
{
    private Camera? _camera;

    public Camera? Camera
    {
        get => _camera;
        set
        {
            if (ReferenceEquals(_camera, value)) return;
            _camera = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Camera)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
