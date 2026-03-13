using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.Hosting;
using Pos.Server.Hosting;
using Pos.Terminal.Models;
using Pos.Terminal.Services;

namespace Pos.Terminal;

public partial class SetupWindow : Window
{
    private readonly SettingsStore _settings = new();
    private IHost? _embeddedServer;

    public SetupWindow()
    {
        InitializeComponent();
    }

    private async void ScanLan_Click(object? sender, RoutedEventArgs e)
    {
        var service = new LanDiscoveryService();
        var servers = await service.ScanAsync();
        DiscoveredList.ItemsSource = servers.Select(x => $"{x.CompanyName} | {x.Ip}:{x.Port}").ToList();
        StatusText.Text = $"Discovered {servers.Count} server(s).";
    }

    private async void Continue_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var isServer = (ModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Server";
            var host = ManualHostBox.Text?.Trim() ?? "127.0.0.1";
            var port = int.TryParse(ManualPortBox.Text, out var p) ? p : 5050;

            if (isServer)
            {
                var company = CompanyNameBox.Text?.Trim() ?? "ModernPOS";
                var superUser = SuperUserBox.Text?.Trim() ?? "admin";
                var superPass = SuperPassBox.Text?.Trim() ?? "admin123";
                port = int.TryParse(ServerPortBox.Text, out var serverPort) ? serverPort : 5050;

                _embeddedServer = await ModernPosServerHost.StartAsync(new ModernPosServerOptions($"Data Source=server-{port}.db", port, company));
                using var api = new RemoteServerApi("127.0.0.1", port);
                await api.BootstrapAsync(company, superUser, superPass, port);
                host = "127.0.0.1";
                StatusText.Text = "Server initialized and listening.";
            }

            using var loginApi = new RemoteServerApi(host, port);
            var login = await loginApi.LoginAsync(LoginUserBox.Text ?? "", LoginPassBox.Text ?? "");

            await _settings.SaveDeploymentAsync(new DeploymentSettings
            {
                IsConfigured = true,
                Mode = isServer ? "Server" : "Client",
                ServerHost = host,
                ServerPort = port,
                CompanyName = login.CompanyName,
                AuthToken = login.Token,
                Username = LoginUserBox.Text ?? "",
                Role = login.Role.ToString()
            });

            var main = new MainWindow();
            main.Show();
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}
