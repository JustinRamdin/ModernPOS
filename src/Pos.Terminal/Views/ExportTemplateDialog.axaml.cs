using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;

namespace Pos.Terminal.Views;

public partial class ExportTemplateDialog : Window
{
    public ExportTemplateDialog()
    {
        InitializeComponent();

        // When DataContext is set, bind dynamic columns to ColumnHeaders
        this.AttachedToVisualTree += (_, __) =>
        {
            if (DataContext is not Pos.Terminal.ViewModels.ExportTemplateDialogViewModel vm)
                return;

            vm.ColumnHeaders.CollectionChanged += (_, __) => BuildColumns(vm);
            BuildColumns(vm);
        };
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
}
