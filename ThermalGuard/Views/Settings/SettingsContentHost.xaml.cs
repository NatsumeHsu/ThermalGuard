using System.Windows;
using System.Windows.Controls;
namespace ThermalGuard.Views.Settings;
public partial class SettingsContentHost : UserControl
{
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(SettingsContentHost), new PropertyMetadata(0, Changed));
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public SettingsContentHost() => InitializeComponent();

    public void ReloadSelectedPage()
    {
        switch (SelectedIndex)
        {
            case 0:
                ContentGrid.Children.Remove(StreamPanel);
                StreamPanel = new StreamSettingsView();
                ContentGrid.Children.Add(StreamPanel);
                break;
            case 1:
                ContentGrid.Children.Remove(EventPanel);
                EventPanel = new EventSettingsView();
                ContentGrid.Children.Add(EventPanel);
                break;
            case 2:
                ContentGrid.Children.Remove(RecordingPanel);
                RecordingPanel = new RecordingSettingsView();
                ContentGrid.Children.Add(RecordingPanel);
                break;
        }
        UpdateVisibility();
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((SettingsContentHost)sender).UpdateVisibility();
    private void UpdateVisibility()
    {
        StreamPanel.Visibility = SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        EventPanel.Visibility = SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        RecordingPanel.Visibility = SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }
}
