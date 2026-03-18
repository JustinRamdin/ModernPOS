using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Pos.Terminal.Models;
using Pos.Terminal.Services;
using Pos.Terminal.ViewModels;

namespace Pos.Terminal;

public partial class MainWindow : Window
{
    private MainWindowViewModel VM => (MainWindowViewModel)DataContext!;

    private readonly DispatcherTimer _serverMonitor = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _serverCheckInProgress;
    private bool _serverOnline = true;
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainWindowViewModel();
        DataContext = vm;

        vm.ShowTerminal();

        Opened += async (_, __) =>
        {
            await vm.LoadAsync();
            await ApplyRoleRestrictionsAsync();
            await vm.LoadSessionHeaderAsync();
            StartServerMonitor();
        };
         Closed += (_, __) => _serverMonitor.Stop();
    }

    private void StartServerMonitor()
    {
        _serverMonitor.Tick -= ServerMonitorTickAsync;
        _serverMonitor.Tick += ServerMonitorTickAsync;
        _serverMonitor.Start();
        _ = CheckServerStateAsync();
    }

    private async void ServerMonitorTickAsync(object? sender, EventArgs e)
        => await CheckServerStateAsync();

    private async Task CheckServerStateAsync()
    {
        if (_serverCheckInProgress)
            return;

        _serverCheckInProgress = true;
        try
        {
            var reachable = await VM.IsServerReachableAsync();
            if (!reachable && _serverOnline)
            {
                _serverOnline = false;
                await VM.HandleServerDisconnectedAsync();
            }
            else if (reachable && !_serverOnline)
            {
                _serverOnline = true;
                await VM.HandleServerReconnectedAsync();
            }
        }
        finally
        {
            _serverCheckInProgress = false;
        }
    }

    private async Task ApplyRoleRestrictionsAsync()
    {
        var deployment = await new SettingsStore().LoadDeploymentAsync();
        var role = deployment.Role;


        SettingsButton.IsVisible = role is "SuperUser";
        UserAdminButton.IsVisible = role is "SuperUser";
        BackupButton.IsVisible = role is "SuperUser" or "Manager";
        UpdatesButton.IsVisible = role is "SuperUser" or "Manager";
        FinancialButton.IsVisible = role is "SuperUser" or "Accountant";
        InventoryButton.IsVisible = role is not "Cashier";
    }

    private async void Logout_Click(object? sender, RoutedEventArgs e)
    {
        if (!await VM.IsServerReachableAsync())
        {
            VM.Toast("Server is offline. Waiting for server...");
            await VM.HandleServerDisconnectedAsync();
            return;
        }

        var settings = new SettingsStore();
        var deployment = await settings.LoadDeploymentAsync();

        deployment.IsConfigured = false;
        deployment.AuthToken = string.Empty;
        deployment.Username = string.Empty;
        deployment.Role = string.Empty;
        await settings.SaveDeploymentAsync(deployment);

        var login = new LoginWindow(deployment.ServerHost, deployment.ServerPort);
        login.Show();
        Close();
    }
   public void NavTerminal_Click(object? sender, RoutedEventArgs e) => VM.ShowTerminal();
   public void NavInventory_Click(object? sender, RoutedEventArgs e) => VM.ShowInventory();
    public void NavCustomers_Click(object? sender, RoutedEventArgs e) => VM.ShowCustomers();
    private void NavReports_Click(object? sender, RoutedEventArgs e) => VM.ShowReports();
    private void NavFinancial_Click(object? sender, RoutedEventArgs e) => VM.ShowFinancial();
    private void NavSettings_Click(object? sender, RoutedEventArgs e) => VM.ShowSettings();
     private void UserAdmin_Click(object? sender, RoutedEventArgs e)
     => VM.ShowUserManagement();

    private void Backup_Click(object? sender, RoutedEventArgs e)
        => VM.ShowBackup();
    private void Updates_Click(object? sender, RoutedEventArgs e)
        => VM.ShowUpdates();
    public void AddCommand(object? param)

    {
        if (param is ProductDto p) VM.AddToCart(p);
    }

    public void RemoveCommand(object? param)
    {
        if (param is CartLine line) VM.RemoveLine(line);
    }

    public void ClearCommand(object? param) => VM.ClearCart();

   public async void Checkout100Command(object? param) => await VM.CheckoutCashAsync(100m);
}
