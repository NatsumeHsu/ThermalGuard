using System.Windows;
using ThermalGuard.Models;

namespace ThermalGuard.Views;

public partial class EventAlertWindow : Window
{
    private readonly Action _confirm;

    public EventAlertWindow(Camera camera, Action confirm)
    {
        InitializeComponent();
        _confirm = confirm;
        CameraText.Text = $"{camera.Id}  {camera.Name}";
        EventText.Text = camera.EventDisplayName;
        TemperatureText.Text = $"目前溫度：{camera.Temperature:F1} °C";
        RegionText.Text = camera.ThermalRegionSummary;
        Loaded += (_, _) => PlaceAtTopRight();
    }

    private void PlaceAtTopRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 16;
        Top = area.Top + 16;
    }

    private void ConfirmClick(object sender, RoutedEventArgs e)
    {
        _confirm();
        Close();
    }
}
