using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using System;

namespace Pos.Terminal.Views;

public partial class ExportTemplateDialog : Window
{
    private Pos.Terminal.ViewModels.ExportTemplateDialogViewModel? _boundVm;

    public ExportTemplateDialog()
    {
        InitializeComponent();

        // DataContext can be assigned after the control is attached, so listen for changes.
        DataContextChanged += (_, __) => RebindColumns();
        AttachedToVisualTree += (_, __) => RebindColumns();
        DetachedFromVisualTree += (_, __) => UnbindColumns();
    }

    private void RebindColumns()
    {
        UnbindColumns();

        if (DataContext is not Pos.Terminal.ViewModels.ExportTemplateDialogViewModel vm)
            return;

        _boundVm = vm;
        vm.ColumnHeaders.CollectionChanged += OnColumnHeadersChanged;
        BuildColumns(vm);
    }

    private void UnbindColumns()
    {
        if (_boundVm is null)
            return;

        _boundVm.ColumnHeaders.CollectionChanged -= OnColumnHeadersChanged;
        _boundVm = null;
    }

    private void OnColumnHeadersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_boundVm is null)
            return;

            BuildColumns(_boundVm);
    }

    private void BuildColumns(Pos.Terminal.ViewModels.ExportTemplateDialogViewModel vm)
    {
        var grid = this.FindControl<DataGrid>("Grid");
        if (grid == null)
            return;

        grid.Columns.Clear();

        foreach (var header in vm.ColumnHeaders)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding($"[{header}]") // ExportRow indexer
            });
        }
    }
    public async void Export_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not Pos.Terminal.ViewModels.ExportTemplateDialogViewModel vm)
            return;

         var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            DefaultExtension = "xlsx",
             SuggestedFileName = $"{vm.TemplateName}-{DateTime.Now:yyyyMMdd}.xlsx",
            FileTypeChoices = new[]
            {
                 new FilePickerFileType("Excel Workbook") { Patterns = new[] { "*.xlsx" } }
            }
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        await vm.ExportAsync(path);
    }
}
