using Avalonia.Controls;
using Avalonia.Interactivity;
using Pos.Contracts;
using Pos.Terminal.Services;

namespace Pos.Terminal;

public partial class UserManagementWindow : Window
{
    public UserManagementWindow() => InitializeComponent();

    private async void Create_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var deploy = await new SettingsStore().LoadDeploymentAsync();
            using var api = new RemoteServerApi(deploy.ServerHost, deploy.ServerPort, deploy.AuthToken);
            var role = (RoleBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Cashier";
            await api.CreateUserAsync(new CreateUserApiRequest(UsernameBox.Text ?? string.Empty, PasswordBox.Text ?? string.Empty, role, DisplayNameBox.Text));
            StatusText.Text = "User created.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}
