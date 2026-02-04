using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

using Avalonia.Controls;
using Avalonia.Interactivity;

using Pos.Terminal.Models;

namespace Pos.Terminal.Views;

public partial class PayDialog : Window, INotifyPropertyChanged
{
    private decimal _totalDue;
    private bool _hasCustomer;
    private decimal _cashTendered;

    public PaymentResult Result { get; private set; } = new(PaymentMethod.None, 0m);

    public string TotalDueText => $"${_totalDue:0.00}";
    public string CustomerText => _hasCustomer ? "Selected" : "None";

    public string ChangeText
    {
        get
        {
            var change = Math.Round(Math.Max(0m, _cashTendered - _totalDue), 2, MidpointRounding.AwayFromZero);
            return $"${change:0.00}";
        }
    }

    // ✅ Required by Avalonia XAML loader
    public PayDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    // Convenience constructor used by TerminalView
    public PayDialog(decimal totalDue, bool hasCustomer) : this()
    {
        Initialize(totalDue, hasCustomer);
    }

    public void Initialize(decimal totalDue, bool hasCustomer)
    {
        _totalDue = Math.Round(Math.Max(0m, totalDue), 2);
        _hasCustomer = hasCustomer;

        // Pre-fill cash with total due
        _cashTendered = _totalDue;

        OnPropertyChanged(nameof(TotalDueText));
        OnPropertyChanged(nameof(CustomerText));
        OnPropertyChanged(nameof(ChangeText));
    }

    // Avoid AvaloniaObject.PropertyChanged warning (optional)
    public new event PropertyChangedEventHandler? PropertyChanged;

    // --------------------
    // Method selection
    // --------------------
    private void Cash_Click(object? sender, RoutedEventArgs e)
    {
        CashPanel.IsVisible = true;
        CashBox.Text = _totalDue.ToString("0.00", CultureInfo.InvariantCulture);
        _cashTendered = _totalDue;

        OnPropertyChanged(nameof(ChangeText));

        CashBox.Focus();
        CashBox.SelectAll();
    }

    private void Debit_Click(object? sender, RoutedEventArgs e)
    {
        Result = new PaymentResult(PaymentMethod.Debit, 0m);
        Close(true);
    }

    private void Credit_Click(object? sender, RoutedEventArgs e)
    {
        Result = new PaymentResult(PaymentMethod.Credit, 0m);
        Close(true);
    }

    private void OnAccount_Click(object? sender, RoutedEventArgs e)
    {
        Result = new PaymentResult(PaymentMethod.OnAccount, 0m);
        Close(true);
    }

    // --------------------
    // Cash flow
    // --------------------
    private void QuickCash_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control c) return;
        if (c.Tag is not string raw) return;
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var val)) return;

        SetCash(val);
    }

    private void ConfirmCash_Click(object? sender, RoutedEventArgs e)
    {
        var tendered = ParseMoney(CashBox.Text);
        if (tendered == null) return;

        if (tendered.Value < _totalDue)
            return; // keep window open; cashier can correct

        Result = new PaymentResult(PaymentMethod.Cash, tendered.Value);
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Result = new PaymentResult(PaymentMethod.None, 0m);
        Close(false);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        CashBox.TextChanged += CashBox_OnTextChanged;
    }

    private void SetCash(decimal tendered)
    {
        _cashTendered = Math.Round(Math.Max(0m, tendered), 2, MidpointRounding.AwayFromZero);
        CashBox.Text = _cashTendered.ToString("0.00", CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(ChangeText));
    }

    private void CashBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        var tendered = ParseMoney(CashBox.Text);
        _cashTendered = tendered ?? 0m;
        OnPropertyChanged(nameof(ChangeText));
    }

    private static decimal? ParseMoney(string? text)
    {
        var raw = (text ?? "").Trim().Replace("$", "");
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var val))
            return null;

        return Math.Round(val, 2, MidpointRounding.AwayFromZero);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
