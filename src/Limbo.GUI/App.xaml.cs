using System.Windows;
using Limbo.Core.Config;

namespace Limbo.GUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"An unhandled exception occurred: {e.Exception.Message}\n\nStack Trace:\n{e.Exception.StackTrace}", 
                        "Limbo Error", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Error);
        e.Handled = true;
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Load config and apply theme
        var config = ConfigManager.Load();
        var themeMode = ThemeManager.ParseTheme(config.Theme);
        ThemeManager.ApplyTheme(themeMode);
        ThemeManager.StartWatching();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeManager.StopWatching();
        base.OnExit(e);
    }
}
