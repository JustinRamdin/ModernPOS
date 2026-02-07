using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ServicesLocalDb = Pos.Local.Services.LocalDb;

namespace Pos.Terminal;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        try
        {
            // Ensure local SQLite database exists and is migrated
            await Pos.Local.Services.LocalDb.MigrateAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Failed to initialize local database:");
            Console.Error.WriteLine(ex);
            throw;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
