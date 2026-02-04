using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    public async void OpenExportTemplate_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm)
            return;

        if (sender is not Control control || control.DataContext is not ExportTemplateDefinition template)
            return;

        var host = TopLevel.GetTopLevel(this) as Window;
        if (host == null)
            return;

        var dialogVm = new ExportTemplateDialogViewModel(
            template,
            vm.FromDate,
            vm.ToDate,
            vm.LocationCode);

        var dialog = new ExportTemplateDialog
        {
            DataContext = dialogVm
        };

        await dialog.ShowDialog(host);
    }
}
