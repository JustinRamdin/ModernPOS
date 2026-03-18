using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class UpdatesView : UserControl
{
    public UpdatesView()
    {
        InitializeComponent();
    }

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null || DataContext is not UpdatesViewModel vm)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select local update folder",
            AllowMultiple = false
        });

        var selectedFolder = folders.Count > 0
            ? folders[0].TryGetLocalPath()
            : null;

        if (!string.IsNullOrWhiteSpace(selectedFolder))
            await vm.SetSelectedFolderAsync(selectedFolder);
    }
}
