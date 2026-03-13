using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Pos.Terminal.Services;

namespace Pos.Terminal;

public partial class App : Avalonia.Application
{
public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new SettingsStore();
            var deploy = await settings.LoadDeploymentAsync();
            desktop.MainWindow = deploy.IsConfigured ? new MainWindow() : new SetupWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
