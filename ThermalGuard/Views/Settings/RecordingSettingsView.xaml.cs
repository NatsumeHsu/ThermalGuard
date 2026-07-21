using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ThermalGuard.ViewModels;

namespace ThermalGuard.Views.Settings;

public partial class RecordingSettingsView : UserControl
{
    private bool _loaded;

    public RecordingSettingsView() => InitializeComponent();

    private async void UserControlLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded || DataContext is not MainViewModel viewModel) return;
        _loaded = true;
        var settings = await viewModel.LoadRecordingSettingsAsync();
        RecordingEnabledBox.IsChecked = settings.Enabled;
        SegmentSecondsBox.Text = settings.SegmentSeconds.ToString();
        BufferMinutesBox.Text = settings.BufferMinutes.ToString();
        PostEventSecondsBox.Text = settings.PostEventSeconds.ToString();
        RecordingDirectoryBox.Text = settings.OutputDirectory;
        ReservedSpaceGbBox.Text = settings.ReservedSpaceGb.ToString();
        OverwriteOldestEventsBox.IsChecked = settings.OverwriteOldestEvents;
        UpdateAvailableSpace();
    }

    private void BrowseRecordingDirectoryClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "選擇錄影資料夾" };
        if (Directory.Exists(RecordingDirectoryBox.Text)) dialog.InitialDirectory = RecordingDirectoryBox.Text;
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) RecordingDirectoryBox.Text = dialog.FolderName;
    }

    private void RecordingDirectoryChanged(object sender, TextChangedEventArgs e) => UpdateAvailableSpace();

    private void UpdateAvailableSpace()
    {
        if (!IsInitialized || AvailableSpaceText is null || RecordingDirectoryBox is null) return;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(RecordingDirectoryBox.Text.Trim()));
            if (string.IsNullOrWhiteSpace(root)) throw new IOException();
            var drive = new DriveInfo(root);
            AvailableSpaceText.Text = $"儲存磁碟：{root}　可用空間：{drive.AvailableFreeSpace / 1024d / 1024 / 1024:F1} GB";
        }
        catch { AvailableSpaceText.Text = "可用空間：無法讀取，請確認儲存位置"; }
    }

    private async void SaveRecordingClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        try
        {
            if (!int.TryParse(SegmentSecondsBox.Text, out var segmentSeconds) || segmentSeconds is < 1 or > 300 ||
                !int.TryParse(BufferMinutesBox.Text, out var bufferMinutes) || bufferMinutes is < 1 or > 1440 ||
                !int.TryParse(PostEventSecondsBox.Text, out var postEventSeconds) || postEventSeconds is < 0 or > 3600 ||
                !int.TryParse(ReservedSpaceGbBox.Text, out var reservedSpaceGb) || reservedSpaceGb is < 0 or > 100000 ||
                string.IsNullOrWhiteSpace(RecordingDirectoryBox.Text))
                throw new ArgumentException("請確認錄影參數：每段 1–300 秒、循環保留 1–1440 分鐘、事件後 0–3600 秒、預留空間 0–100000 GB");
            var directory = Path.GetFullPath(RecordingDirectoryBox.Text.Trim());
            Directory.CreateDirectory(directory);
            await viewModel.SaveRecordingSettingsAsync(new(
                RecordingEnabledBox.IsChecked == true, segmentSeconds, bufferMinutes, postEventSeconds,
                directory, reservedSpaceGb, OverwriteOldestEventsBox.IsChecked == true));
            MessageBox.Show(Window.GetWindow(this), "錄影設定已儲存並套用", "錄影設定",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "儲存失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
