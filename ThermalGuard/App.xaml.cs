using System.Windows;
using LibVLCSharp.Shared;

namespace ThermalGuard;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
        Core.Initialize();
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "ThermalGuard Pro", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
