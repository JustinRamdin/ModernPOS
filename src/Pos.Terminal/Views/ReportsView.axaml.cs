using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    
    public void OpenExportTemplate_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control control)
        {
            FlyoutBase.ShowAttachedFlyout(control);
        }
    }

    public async void OpenExportWorkspace_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control control || control.Tag is not ExportTemplateDefinition template)
            return;

        await OpenExportTemplateDialogAsync(template);
    }

    public async void OpenExportLast7Days_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control control || control.Tag is not ExportTemplateDefinition template || DataContext is not ReportsViewModel vm)
            return;

        vm.FromDate = DateTime.Today.AddDays(-6);
        vm.ToDate = DateTime.Today;

        await OpenExportTemplateDialogAsync(template);
    }

    public async void OpenExportThisMonth_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control control || control.Tag is not ExportTemplateDefinition template || DataContext is not ReportsViewModel vm)
            return;

        vm.FromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        vm.ToDate = DateTime.Today;

        await OpenExportTemplateDialogAsync(template);
    }

    private async Task OpenExportTemplateDialogAsync(ExportTemplateDefinition template)
    {
        if (DataContext is not ReportsViewModel vm)
            return;

        var host = TopLevel.GetTopLevel(this) as Window;
        if (host == null)
            return;

        var dialogVm = new ExportTemplateDialogViewModel(
            template: template,
            locationCode: vm.LocationCode,
            fromDate: vm.FromDate,
            toDate: vm.ToDate);


        var dialog = new ExportTemplateDialog
        {
            DataContext = dialogVm
        };

        await dialog.ShowDialog(host);
    }
}
