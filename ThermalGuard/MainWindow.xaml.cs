using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ThermalGuard.Models;
using ThermalGuard.Controls;
using ThermalGuard.ViewModels;
using ThermalGuard.Views;

namespace ThermalGuard;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private Point _dragStart;
    private int _gridColumnsBeforeMaximize;
    private bool _isReadyToClose;
    private SettingsWindow? _settingsWindow;

    public MainWindow() { InitializeComponent(); DataContext = _viewModel; }
    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            await _viewModel.ReconnectDisplayedCamerasAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "啟動失敗", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isReadyToClose) return;

        e.Cancel = true;
        IsEnabled = false;

        try
        {
            await _viewModel.DisposeAsync();
            _isReadyToClose = true;
            Close();
        }
        catch (Exception ex)
        {
            IsEnabled = true;
            MessageBox.Show(this, ex.Message, "關閉失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_viewModel) { Owner = this };
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            WindowState = WindowState.Maximized;
            Activate();
        };
        _settingsWindow.Show();
    }

    internal async Task AddCameraAsync(Window owner)
    {
        var dialog = new AddCameraDialog(_viewModel.AvailableIds()) { Owner = owner };
        var accepted = dialog.ShowDialog() == true;
        if (!accepted) return;
        try
        {
            await _viewModel.AddCameraAsync(dialog.CameraId, dialog.CameraName, dialog.Rtsp, dialog.SourceType, dialog.ImagingType, dialog.StreamType);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            await _viewModel.ReconnectDisplayedCamerasAsync();
        }
        catch (Exception ex) { MessageBox.Show(owner, ex.Message, "新增失敗", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    internal async Task EditCameraAsync(Window owner, Camera camera)
    {
        var dialog = new AddCameraDialog(camera) { Owner = owner };
        var accepted = dialog.ShowDialog() == true;
        if (!accepted) return;
        try
        {
            await _viewModel.UpdateCameraAsync(camera, dialog.CameraName, dialog.Rtsp,
                dialog.SourceType, dialog.ImagingType, dialog.StreamType);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            await _viewModel.ReconnectDisplayedCamerasAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "編輯失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void TitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState.Maximized;
            return;
        }

        var keepMaximized = WindowState == WindowState.Maximized;
        if (keepMaximized)
        {
            var pointer = e.GetPosition(this);
            var screenPointer = PointToScreen(pointer);
            var horizontalRatio = pointer.X / ActualWidth;
            WindowState = WindowState.Normal;
            Left = screenPointer.X - Width * horizontalRatio;
            Top = Math.Max(0, screenPointer.Y - 24);
        }
        try
        {
            DragMove();
        }
        finally
        {
            if (keepMaximized) WindowState = WindowState.Maximized;
        }
    }
    private void AboutClick(object sender, RoutedEventArgs e) => MessageBox.Show(this, "ThermalGuard Pro\nC# WPF + LibVLCSharp\n雙光熱像即時監看", "關於", MessageBoxButton.OK, MessageBoxImage.Information);
    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        CollectionViewSource.GetDefaultView(CameraList.ItemsSource).Filter = item => item is Camera camera && (string.IsNullOrWhiteSpace(SearchBox.Text) || camera.Id.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase) || camera.Name.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase));
    }
    private void CameraListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && CameraList.SelectedItem is Camera camera) DragDrop.DoDragDrop(CameraList, camera, DragDropEffects.Move);
    }
    private void TileMouseDown(object sender, MouseButtonEventArgs e) { _dragStart = e.GetPosition(this); if (((FrameworkElement)sender).DataContext is Camera camera) _viewModel.SelectedCamera = camera; }
    private void TileMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.MaximizedCamera is not null)
        {
            _viewModel.MaximizedCamera = null;
            _viewModel.GridColumns = _gridColumnsBeforeMaximize;
        }
        else if (((FrameworkElement)sender).DataContext is Camera camera)
        {
            _gridColumnsBeforeMaximize = _viewModel.GridColumns;
            _viewModel.SelectedCamera = camera;
            _viewModel.GridColumns = 1;
            _viewModel.MaximizedCamera = camera;
        }

        e.Handled = true;
    }
    private void TileMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || ((FrameworkElement)sender).DataContext is not Camera camera) return;
        var p = e.GetPosition(this); if (Math.Abs(p.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance) DragDrop.DoDragDrop((DependencyObject)sender, camera, DragDropEffects.Move);
    }
    private void WallDragOver(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(typeof(Camera)) ? DragDropEffects.Move : DragDropEffects.None;
    private async void WallDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Camera)) is not Camera source) return;
        var element = InputHitTest(e.GetPosition(this)) as DependencyObject;
        while (element is not null && element is not CameraTile) element = VisualTreeHelper.GetParent(element);
        if (element is not CameraTile tile) return;

        var container = ItemsControl.ContainerFromElement(CameraWall, tile);
        var targetIndex = CameraWall.ItemContainerGenerator.IndexFromContainer(container);
        await _viewModel.ReplaceDisplayedCameraAsync(source, targetIndex);
    }
}
