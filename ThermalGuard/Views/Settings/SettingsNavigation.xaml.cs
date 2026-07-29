using System.Windows.Controls;
namespace ThermalGuard.Views.Settings;
public partial class SettingsNavigation : UserControl
{
    public event SelectionChangedEventHandler? SelectionChanged;
    public int SelectedIndex => Menu.SelectedIndex;
    public SettingsNavigation() => InitializeComponent();
    private void MenuSelectionChanged(object sender, SelectionChangedEventArgs e) => SelectionChanged?.Invoke(this, e);
}
