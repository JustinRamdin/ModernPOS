using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
    public async void TogglePracticeMode_Click(object? sender, RoutedEventArgs e)
    {
        await VM.TogglePracticeModeAsync();
        RestartApplication();
    }

    private static void RestartApplication()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var args = Environment.GetCommandLineArgs().Skip(1);
            Process.Start(path, string.Join(" ", args));
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
