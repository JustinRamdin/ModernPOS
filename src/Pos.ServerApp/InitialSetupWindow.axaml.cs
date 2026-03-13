using Avalonia.Controls;
using Avalonia.Interactivity;
using Pos.Contracts;
using Pos.Server.Hosting;
using Pos.ServerApp.Services;

namespace Pos.ServerApp;

public partial class InitialSetupWindow : Window
{
    public InitialSetupWindow() => InitializeComponent();

    private async void Initialize_Click(object? sender, RoutedEventArgs e)
    {
        if (PasswordBox.Text != ConfirmPasswordBox.Text)
        {
            StatusText.Text = "Passwords do not match.";
            return;
        }

        var port = int.TryParse(PortBox.Text, out var parsed) ? parsed : 5050;
        var company = string.IsNullOrWhiteSpace(CompanyNameBox.Text) ? "ModernPOS" : CompanyNameBox.Text.Trim();
        var settings = new ServerAppSettings { IsConfigured = true, CompanyName = company, Port = port, ConnectionString = $"Data Source=server-{port}.db" };

        var server = await ModernPosServerHost.StartAsync(new ModernPosServerOptions(settings.ConnectionString, settings.Port, settings.CompanyName));
        var api = new ServerAdminApi("127.0.0.1", settings.Port);
         var initialized = await api.BootstrapAsync(new BootstrapServerRequest(company, SuperUserBox.Text ?? "admin", PasswordBox.Text ?? string.Empty, port));

        if (!initialized)
        {
            StatusText.Text = "Server is already initialized. Opening dashboard.";
        }

        new ServerAppSettingsStore().Save(settings);

        var dashboard = new DashboardWindow(settings, server);
        dashboard.Show();
        Close();
    }
}
