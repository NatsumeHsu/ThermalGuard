using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ThermalGuard.ViewModels;
using Microsoft.Win32;

namespace ThermalGuard.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void TitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private async void ExportSettingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var dialog = new SaveFileDialog
        {
            Title = "匯出 ThermalGuard 設定",
            Filter = "ThermalGuard 設定檔 (*.csv)|*.csv",
            FileName = $"ThermalGuard-settings-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await viewModel.ExportSettingsAsync(dialog.FileName);
            MessageBox.Show(this, "全部設定已匯出", "匯出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "匯出失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ImportSettingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var dialog = new OpenFileDialog
        {
            Title = "匯入 ThermalGuard 設定",
            Filter = "ThermalGuard 設定檔 (*.csv)|*.csv",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true ||
            MessageBox.Show(this, "匯入會取代目前全部設定，是否繼續？", "確認匯入",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var backup = await viewModel.ImportSettingsAsync(dialog.FileName);
            MessageBox.Show(this, $"設定匯入完成。\n目前設定已備份至：\n{backup}\n\n請重新啟動程式套用設定。",
                "匯入完成", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "匯入失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SettingsMenuChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || StreamSettingsPanel is null || RecordingSettingsPanel is null ||
            EventSettingsPanel is null || ModbusSettingsPanel is null) return;
        StreamSettingsPanel.Visibility = SettingsMenu.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        EventSettingsPanel.Visibility = SettingsMenu.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        ModbusSettingsPanel.Visibility = SettingsMenu.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        RecordingSettingsPanel.Visibility = SettingsMenu.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    }
}
