using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private SettingsViewModel VM => (SettingsViewModel)DataContext!;

    public async void UploadHeaderImage_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select header image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file == null)
            return;

        await VM.SetHeaderImageAsync(file.Path.LocalPath);
    }

    public void RefreshPrinters_Click(object? sender, RoutedEventArgs e)
    {
        VM.LoadPrinters();
    }

    public async void Save_Click(object? sender, RoutedEventArgs e)
    {
        await VM.SaveAsync();
    }
}
