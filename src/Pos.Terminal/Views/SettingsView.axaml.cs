using Avalonia.Controls;
using Avalonia.Interactivity;
using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private SettingsViewModel VM => (SettingsViewModel)DataContext!;

    public void RefreshPrinters_Click(object? sender, RoutedEventArgs e)
    {
        VM.LoadPrinters();
    }

    public async void RefreshSharedProfile_Click(object? sender, RoutedEventArgs e)
    {
        await VM.RefreshSharedProfileAsync();
    }

    public async void Save_Click(object? sender, RoutedEventArgs e)
    {
        await VM.SaveAsync();
    }
}
