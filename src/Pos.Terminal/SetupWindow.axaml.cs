using Avalonia.Controls;
using Avalonia.Interactivity;
using Pos.Terminal.Models;
using Pos.Terminal.Services;

namespace Pos.Terminal;

public partial class SetupWindow : Window
{
    private readonly SettingsStore _settings = new();
     private List<DiscoveredServer> _servers = [];


    public SetupWindow() => InitializeComponent();

    private async void ScanLan_Click(object? sender, RoutedEventArgs e)
    {
        var service = new LanDiscoveryService();
        _servers = (await service.ScanAsync()).ToList();
        DiscoveredList.ItemsSource = _servers.Select(x => $"{x.CompanyName} | {x.Ip}:{x.Port}").ToList();
        StatusText.Text = $"Discovered {_servers.Count} server(s).";
    }

    private void DiscoveredList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DiscoveredList.SelectedIndex < 0 || DiscoveredList.SelectedIndex >= _servers.Count) return;
        var selected = _servers[DiscoveredList.SelectedIndex];
        ManualHostBox.Text = selected.Ip;
        ManualPortBox.Text = selected.Port.ToString();
    }

    private async void Continue_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var host = ManualHostBox.Text?.Trim() ?? "127.0.0.1";
            var port = int.TryParse(ManualPortBox.Text, out var parsed) ? parsed : 5050;

            using var loginApi = new RemoteServerApi(host, port);
            var login = await loginApi.LoginAsync(LoginUserBox.Text ?? "", LoginPassBox.Text ?? "");

            await _settings.SaveDeploymentAsync(new DeploymentSettings
            {
                IsConfigured = true,
                Mode = "Client",
                ServerHost = host,
                ServerPort = port,
                CompanyName = login.CompanyName,
                AuthToken = login.Token,
                Username = login.Username,
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
