using Avalonia.Controls;
using Avalonia.Interactivity;
using Pos.Terminal.Models;
using Pos.Terminal.ViewModels;

namespace Pos.Terminal;

public partial class MainWindow : Window
{
    private MainWindowViewModel VM => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainWindowViewModel();
        DataContext = vm;

        // default page
        vm.ShowTerminal();

        Opened += async (_, __) => await vm.LoadAsync();
    }

    // Sidebar navigation
    public void NavTerminal_Click(object? sender, RoutedEventArgs e) => VM.ShowTerminal();
    public void NavInventory_Click(object? sender, RoutedEventArgs e) => VM.ShowInventory();
    public void NavCustomers_Click(object? sender, RoutedEventArgs e) => VM.ShowCustomers();

    private void NavReports_Click(object? sender, RoutedEventArgs e)
    {
        VM.ShowReports();
    }
    private void NavSettings_Click(object? sender, RoutedEventArgs e)
    {
        VM.ShowSettings();
    }

    // Keep your existing "commands" used by TerminalView.axaml
    public void AddCommand(object? param)
    {
        if (param is ProductDto p) VM.AddToCart(p);
    }

    public void RemoveCommand(object? param)
    {
        if (param is CartLine line) VM.RemoveLine(line);
    }

    public void ClearCommand(object? param) => VM.ClearCart();

    public async void Checkout100Command(object? param)
    {
        await VM.CheckoutCashAsync(100m);
    }
}
