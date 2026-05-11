using System.Net;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
         if (!await api.IsInitializedAsync())
        {
            try
            {
                await api.BootstrapAsync(new BootstrapServerRequest(company, SuperUserBox.Text ?? "admin", PasswordBox.Text ?? string.Empty, port));
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                // Server was initialized by another process between status check and bootstrap call.
            }
        }


        new ServerAppSettingsStore().Save(settings);

        var dashboard = new DashboardWindow(settings, server);
        
         if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = dashboard;
        }

        dashboard.Show();
        Close();
    }
}
