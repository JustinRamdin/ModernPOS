using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pos.Terminal.Views;

public sealed record MiscItemResult(string Name, string Description, decimal Quantity, decimal UnitPrice, bool VatInclusive);

public partial class MiscItemDialog : Window
{
    public MiscItemDialog()
    {
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus();
    }

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Enter an item name.");
            return;
        }

        if (!TryParseDecimal(QuantityBox.Text, out var quantity) || quantity <= 0m)
        {
            ShowError("Quantity must be greater than zero.");
            return;
        }

        if (!TryParseDecimal(PriceBox.Text, out var price) || price < 0m)
        {
            ShowError("Unit price must be zero or greater.");
            return;
        }

        Close(new MiscItemResult(name, DescriptionBox.Text?.Trim() ?? "", quantity,
            Math.Round(price, 2, MidpointRounding.AwayFromZero), VatInclusiveBox.IsChecked == true));
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private static bool TryParseDecimal(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value) ||
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
