using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class BackupView : UserControl
{
    public BackupView()
    {
        InitializeComponent();
    }
    private async void BrowseBackupFolder_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null || DataContext is not BackupViewModel vm)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select backup destination folder",
            AllowMultiple = false
        });

        var selectedFolder = folders.Count > 0
            ? folders[0].TryGetLocalPath()
            : null;

        if (!string.IsNullOrWhiteSpace(selectedFolder))
            vm.SetBackupFolder(selectedFolder);
    }

    private void UseDefaultBackupFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BackupViewModel vm)
            vm.SetBackupFolder(string.Empty);
    }

    private async void BrowseRestoreFolder_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null || DataContext is not BackupViewModel vm)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select folder that contains backups",
            AllowMultiple = false
        });

        var selectedFolder = folders.Count > 0
            ? folders[0].TryGetLocalPath()
            : null;

        if (!string.IsNullOrWhiteSpace(selectedFolder))
            vm.SetRestoreFolder(selectedFolder);
    }

    private async void BrowseRestoreFile_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null || DataContext is not BackupViewModel vm)
            return;

        var startLocation = !string.IsNullOrWhiteSpace(vm.RestoreFolder) && Directory.Exists(vm.RestoreFolder)
            ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(vm.RestoreFolder)
            : null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select backup file to restore",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation,
            FileTypeFilter =
            [
                new FilePickerFileType("Database backups") { Patterns = ["*.db"] },
                FilePickerFileTypes.All
            ]
        });

        var selectedFile = files.Count > 0
            ? files[0].TryGetLocalPath()
            : null;

        if (!string.IsNullOrWhiteSpace(selectedFile))
            vm.SetRestoreFile(selectedFile);
    }
}
