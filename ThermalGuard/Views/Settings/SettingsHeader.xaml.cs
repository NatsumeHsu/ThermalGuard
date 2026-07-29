using System.Windows;
using System.Windows.Controls;
namespace ThermalGuard.Views.Settings;
public partial class SettingsHeader : UserControl
{
    public event RoutedEventHandler? ImportRequested;
    public event RoutedEventHandler? ExportRequested;
    public event RoutedEventHandler? CloseRequested;
    public SettingsHeader() => InitializeComponent();
    private void ImportClick(object sender, RoutedEventArgs e) => ImportRequested?.Invoke(this, e);
    private void ExportClick(object sender, RoutedEventArgs e) => ExportRequested?.Invoke(this, e);
    private void CloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);
}
