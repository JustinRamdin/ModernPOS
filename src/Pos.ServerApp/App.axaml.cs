using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Pos.ServerApp.Services;

namespace Pos.ServerApp;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new ServerAppSettingsStore();
            var settings = store.Load();
            desktop.MainWindow = settings.IsConfigured ? new DashboardWindow(settings) : new InitialSetupWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
