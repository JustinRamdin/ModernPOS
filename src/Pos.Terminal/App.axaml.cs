using Pos.Terminal.Services;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Pos.Terminal;

public partial class App : Avalonia.Application
{
public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new SettingsStore();
            var deployment = settings.LoadDeploymentAsync().GetAwaiter().GetResult();

            if (deployment.IsConfigured && !string.IsNullOrWhiteSpace(deployment.AuthToken))
                desktop.MainWindow = new MainWindow();
            else if (!string.IsNullOrWhiteSpace(deployment.ServerHost))
                desktop.MainWindow = new LoginWindow(deployment.ServerHost, deployment.ServerPort);
            else
                desktop.MainWindow = new SetupWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
