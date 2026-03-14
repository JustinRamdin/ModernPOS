using Avalonia.Controls;
using Avalonia.Interactivity;
using Pos.Terminal.Models;
using Pos.Terminal.Services;

namespace Pos.Terminal;

public partial class LoginWindow : Window
{
    private readonly SettingsStore _settings = new();
    private readonly string _host;
    private readonly int _port;

    public LoginWindow(string host, int port)
    {
        _host = host;
        _port = port;

        InitializeComponent();

        ServerText.Text = $"Connected to {_host}:{_port}";
    }

    private async void Login_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            using var api = new RemoteServerApi(_host, _port);
            var login = await api.LoginAsync(LoginUserBox.Text ?? string.Empty, LoginPassBox.Text ?? string.Empty);

            await _settings.SaveDeploymentAsync(new DeploymentSettings
            {
                IsConfigured = true,
                Mode = "Client",
                ServerHost = _host,
                ServerPort = _port,
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
            StatusText.Text = $"Login failed: {ex.Message}";
        }
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
    {
        var setup = new SetupWindow();
        setup.Show();
        Close();
    }
}
