using System;
using System.Collections.Specialized;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class ExportTemplateDialog : Window
{
    private ExportTemplateDialogViewModel? VM => DataContext as ExportTemplateDialogViewModel;

    public ExportTemplateDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (VM == null)
            return;

        Title = $"{VM.TemplateName} Export";
        VM.ColumnHeaders.CollectionChanged += OnHeadersChanged;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (VM == null)
            return;

        await VM.LoadAsync();
        BuildColumns();
    }

    private void OnHeadersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => BuildColumns();

    private void BuildColumns()
    {
        if (VM == null)
            return;

        var grid = this.FindControl<DataGrid>("TemplateGrid");
        if (grid == null)
            return;

        grid.Columns.Clear();
        foreach (var header in VM.ColumnHeaders)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                   Binding = new Binding($"Values[\"{header}\"]")
                {
                    Mode = BindingMode.OneWay
                }
            });
        }
    }

    public async void ExportToExcel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (VM == null)
            return;

        var storageProvider = StorageProvider;
        if (storageProvider == null)
            return;

        var options = new FilePickerSaveOptions
        {
            DefaultExtension = "xlsx",
            SuggestedFileName = $"{VM.TemplateName}-{DateTime.Now:yyyyMMdd}.xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Excel Workbook") { Patterns = ["*.xlsx"] }
            ]
        };

        var file = await storageProvider.SaveFilePickerAsync(options);
        if (file == null)
            return;

        await VM.ExportAsync(file.Path.LocalPath);
    }
}
