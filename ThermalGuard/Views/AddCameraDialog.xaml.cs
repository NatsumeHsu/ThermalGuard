using System.Windows;
using System.Windows.Controls;
using System.Net;
using MaterialDesignThemes.Wpf;
using ThermalGuard.Models;
using System.Windows.Input;

namespace ThermalGuard.Views;

public partial class AddCameraDialog : Window
{
    public IEnumerable<string> AvailableIds { get; }

    public string CameraId = "";
    public string CameraName => NameBox.Text;
    public string SourceType => ((ComboBoxItem)SourceTypeBox.SelectedItem).Content.ToString()!;
    public string Rtsp
    {
        get
        {
            if (SourceType == "RTSP 串流") return RtspBox.Text;

            var username = UsernameBox.Text.Trim();
            var password = PasswordBox.Password;
            var ip = IpBox.Text.Trim();
            if (username.Length == 0 || password.Length == 0 || ip.Length == 0)
                throw new ArgumentException("帳號、密碼與 IP 不可空白");
            if (!IPAddress.TryParse(ip, out _))
                throw new ArgumentException("IP 位址格式不正確");
            if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
                throw new ArgumentException("Port 必須是 1 到 65535 之間的數字");

            var channel = ImagingType == "熱成像" ? "2" : "1";
            channel += StreamType == "子碼流" ? "02" : "01";
            return new UriBuilder("rtsp", ip, port, $"Streaming/Channels/{channel}")
            {
                UserName = username,
                Password = password
            }.Uri.AbsoluteUri;
        }
    }
    public string ImagingType => ((ComboBoxItem)ImagingTypeBox.SelectedItem).Content.ToString()!;
    public string StreamType => ((ComboBoxItem)StreamTypeBox.SelectedItem).Content.ToString()!;
    public AddCameraDialog(IEnumerable<string> ids) { AvailableIds = ids; InitializeComponent(); DataContext = this; }
    public AddCameraDialog(Camera camera) : this([camera.Id])
    {
        Title = "編輯串流";
        CameraId = camera.Id;
        IdBox.SelectedIndex = 0;
        IdBox.IsEnabled = false;
        NameBox.Text = camera.Name;
        SourceTypeBox.SelectedIndex = camera.SourceType == "熱像儀" ? 1 : 0;
        ImagingTypeBox.SelectedIndex = camera.ImagingType == "熱成像" ? 1 : 0;
        StreamTypeBox.SelectedIndex = camera.StreamType == "子碼流" ? 1 : 0;

        if (camera.SourceType == "RTSP 串流")
        {
            RtspBox.Text = camera.Rtsp;
            return;
        }

        var uri = new Uri(camera.Rtsp);
        var credentials = Uri.UnescapeDataString(uri.UserInfo).Split(':', 2);
        UsernameBox.Text = credentials.ElementAtOrDefault(0) ?? "";
        PasswordBox.Password = credentials.ElementAtOrDefault(1) ?? "";
        IpBox.Text = uri.Host;
        PortBox.Text = uri.Port.ToString();
    }
    private void IdSelectionChanged(object sender, SelectionChangedEventArgs e) { if (((ComboBox)sender).SelectedItem is string id) CameraId = id; }
    private void TitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
    private void SourceTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || RtspBox is null || ThermalOptions is null || ThermalCameraFields is null) return;
        var useDirectRtsp = SourceType == "RTSP 串流";
        HintAssist.SetHint(NameBox, useDirectRtsp ? "串流名稱" : "熱像儀名稱");
        RtspBox.Visibility = useDirectRtsp ? Visibility.Visible : Visibility.Collapsed;
        ThermalOptions.Visibility = useDirectRtsp ? Visibility.Collapsed : Visibility.Visible;
        ThermalCameraFields.Visibility = useDirectRtsp ? Visibility.Collapsed : Visibility.Visible;
    }
    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    private void SaveClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
