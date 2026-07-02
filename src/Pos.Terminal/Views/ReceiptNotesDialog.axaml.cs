using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pos.Terminal.Views;

public partial class ReceiptNotesDialog : Window
{
    public ReceiptNotesDialog() : this(string.Empty) { }

    public ReceiptNotesDialog(string footer)
    {
        InitializeComponent();
        FooterBox.Text = footer;
        Opened += (_, _) => FooterBox.Focus();
    }

    private void Save_Click(object? sender, RoutedEventArgs e) => Close(FooterBox.Text ?? string.Empty);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
