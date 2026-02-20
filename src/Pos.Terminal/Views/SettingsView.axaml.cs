using System.Threading.Tasks;
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
        var imagePath = await PickImageAsync("Select header image");
        if (imagePath == null)
            return;

        await VM.SetHeaderImageAsync(imagePath);
    }

    public async void UploadLogoImage_Click(object? sender, RoutedEventArgs e)
    {
        var imagePath = await PickImageAsync("Select receipt logo");
        if (imagePath == null)
            return;

        await VM.SetLogoImageAsync(imagePath);
    }

    public void SetLogoScale1x_Click(object? sender, RoutedEventArgs e) => VM.SetLogoScaleMultiplier(1);
    public void SetLogoScale2x_Click(object? sender, RoutedEventArgs e) => VM.SetLogoScaleMultiplier(2);
    public void SetLogoScale3x_Click(object? sender, RoutedEventArgs e) => VM.SetLogoScaleMultiplier(3);
    public void SetLogoScale4x_Click(object? sender, RoutedEventArgs e) => VM.SetLogoScaleMultiplier(4);


    private async Task<string?> PickImageAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
            return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"]
                }
            ]
        });

        return files.FirstOrDefault()?.Path.LocalPath;
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
