using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Pos.Terminal.ViewModels;

namespace Pos.Terminal.Views;

public partial class FinancialItemsWindow : Window, INotifyPropertyChanged
{
    private readonly FinancialViewModel _vm;
    public FinancialDocumentEditorViewModel Editor { get; }

    private Guid? _selectedProductId;
    public string SelectedProductDisplay => _vm.Products.FirstOrDefault(p => p.Id == _selectedProductId)?.DisplayName ?? "None";

    public new event PropertyChangedEventHandler? PropertyChanged;

    public FinancialItemsWindow(FinancialViewModel vm, FinancialDocumentEditorViewModel editor)
    {
        InitializeComponent();
        _vm = vm;
        Editor = editor;
        _selectedProductId = editor.SelectedProductId;
        DataContext = this;
    }

    private async void Search_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var searchWindow = new FinancialProductSearchWindow(_vm.Products);
            var result = await searchWindow.ShowDialog<Guid?>(this);
            if (result == null)
                return;

            _selectedProductId = result;
            Editor.SelectedProductId = result;
            Raise(nameof(SelectedProductDisplay));
        }
        catch (Exception ex)
        {
            _vm.Status = $"Unable to open product search. {ex.Message}";
        }
    }

    private void AddItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedProductId == null)
            return;

        _vm.AddLine(Editor, _selectedProductId.Value, Editor.LineQuantity, Editor.LineUnitPrice);
    }

    private void EditQuantity_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: FinancialLineItem line })
            return;

        Editor.SelectedLine = line;
        Editor.LineQuantity = line.Quantity;
    }

    private void DeleteLine_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: FinancialLineItem line })
            return;

        _vm.RemoveLine(Editor, line);
    }

    private void Clear_Click(object? sender, RoutedEventArgs e)
        => _vm.ClearLines(Editor);

    private void Save_Click(object? sender, RoutedEventArgs e)
        => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e)
        => Close(false);

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
