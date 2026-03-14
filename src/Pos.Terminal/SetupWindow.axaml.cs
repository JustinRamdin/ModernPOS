using Avalonia.Controls;
using Avalonia.Interactivity;
using Pos.Terminal.Models;
using Pos.Terminal.Services;

namespace Pos.Terminal;

public partial class SetupWindow : Window
{
    private readonly SettingsStore _settings = new();
    private readonly LanDiscoveryService _discoveryService = new();
    private List<DiscoveredServer> _servers = [];

    public SetupWindow()
    {
        InitializeComponent();


    Opened += async (_, __) =>
        {
            var deployment = await _settings.LoadDeploymentAsync();
            ManualHostBox.Text = deployment.ServerHost;
            ManualPortBox.Text = deployment.ServerPort.ToString();
        };
    }

    private async void ScanLan_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Scanning LAN for active servers...";
            _servers = (await _discoveryService.ScanAsync()).ToList();
            DiscoveredList.ItemsSource = _servers.Select(x => $"{x.CompanyName} | {x.Ip}:{x.Port}").ToList();
            StatusText.Text = $"Discovered {_servers.Count} active server(s).";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void DiscoveredList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DiscoveredList.SelectedIndex < 0 || DiscoveredList.SelectedIndex >= _servers.Count)
            return;
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

            using var api = new RemoteServerApi(host, port);
            await api.ValidateServerAsync();

            var deployment = await _settings.LoadDeploymentAsync();
            deployment.IsConfigured = false;
            deployment.Mode = "Client";
            deployment.ServerHost = host;
            deployment.ServerPort = port;
            deployment.AuthToken = string.Empty;
            deployment.Username = string.Empty;
            deployment.Role = string.Empty;
            await _settings.SaveDeploymentAsync(deployment);

            var login = new LoginWindow(host, port);
            login.Show();
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Connection failed: {ex.Message}";
        }
    }
}
