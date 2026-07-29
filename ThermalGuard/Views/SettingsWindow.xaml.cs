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
        var (Entity, DisplayName) = CurrentPage();
        var dialog = new SaveFileDialog
        {
            Title = $"匯出{DisplayName}",
            Filter = "CSV 檔案 (*.csv)|*.csv",
            FileName = $"{Entity}-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await viewModel.ExportSettingsAsync(dialog.FileName, Entity);
            MessageBox.Show(this,
                $"{DisplayName}已匯出至：\n{dialog.FileName}",
                "匯出完成",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "匯出失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ImportSettingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var (Entity, DisplayName) = CurrentPage();
        var dialog = new OpenFileDialog
        {
            Title = $"匯入{DisplayName}",
            Filter = "CSV 檔案 (*.csv)|*.csv",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true ||
            MessageBox.Show(this, $"匯入會更新目前的{DisplayName}，是否繼續？", "確認匯入",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var backup = await viewModel.ImportSettingsAsync(dialog.FileName, Entity);
            SettingsContent.ReloadSelectedPage();
            MessageBox.Show(this, $"{DisplayName}匯入完成並已立即套用。\n目前設定已備份至：\n{backup}",
                "匯入完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            var detail = ex.GetBaseException().Message;
            MessageBox.Show(this, detail == ex.Message ? ex.Message : $"{ex.Message}\n\n{detail}",
                "匯入失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SettingsMenuChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsContent is not null)
            SettingsContent.SelectedIndex = SettingsNavigation.SelectedIndex;
    }

    private (string Entity, string DisplayName) CurrentPage() =>
        SettingsNavigation.SelectedIndex switch
        {
            0 => ("camera", "串流設定"),
            1 => ("region_alarm_setting", "事件與警報設定"),
            2 => ("recording_setting", "錄影設定"),
            _ => throw new InvalidOperationException("目前沒有可匯入或匯出的設定頁面")
        };
}
