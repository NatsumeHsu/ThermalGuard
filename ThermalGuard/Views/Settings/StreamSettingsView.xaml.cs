using System.Windows;
using System.Windows.Controls;
using ThermalGuard.Models;
using ThermalGuard.ViewModels;

namespace ThermalGuard.Views.Settings;

public partial class StreamSettingsView : UserControl
{
    public StreamSettingsView() => InitializeComponent();

    private async void AddStreamClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is SettingsWindow { Owner: MainWindow mainWindow } settings)
            await mainWindow.AddCameraAsync(settings);
    }

    private async void EditCameraClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { CommandParameter: Camera camera } &&
            Window.GetWindow(this) is SettingsWindow { Owner: MainWindow mainWindow } settings)
            await mainWindow.EditCameraAsync(settings, camera);
    }

    private async void RemoveCameraClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { CommandParameter: Camera camera } || DataContext is not MainViewModel viewModel ||
            Window.GetWindow(this) is not SettingsWindow { Owner: MainWindow mainWindow } settings) return;
        if (MessageBox.Show(settings, $"確定要移除 {camera.Id} · {camera.Name}？\n其他攝影機通道不會重新排序。",
                "移除攝影機", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await viewModel.RemoveCameraAsync(camera);
            await mainWindow.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            await viewModel.ReconnectDisplayedCamerasAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(settings, ex.Message, "移除失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
