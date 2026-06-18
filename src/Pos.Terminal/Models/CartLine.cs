using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Pos.Application.Measurements;

namespace Pos.Terminal.Models;

public sealed class CartLine : INotifyPropertyChanged
{
    public Guid ProductId { get; set; }
    public int InventoryBucket { get; set; } = 1;
     public string ItemNumber { get; set; } = "";
    public string Name { get; set; } = "";

    public string ItemName => Name;

    public string ItemDescription { get; set; } = "";

    private string _unit = "ea";
    public string Unit
    {
        get => _unit;
        set
        {
            if (_unit == value) return;
            _unit = value;
            OnPropertyChanged();
        }
    }

    private bool _vatInclusive;
    public bool VatInclusive
    {
        get => _vatInclusive;
        set
        {
            if (_vatInclusive == value) return;
            _vatInclusive = value;
            OnPropertyChanged();
        }
    }

    public decimal UnitPrice { get; set; }
    public decimal SalePrice => UnitPrice;

    private decimal _taxAmount;
    public decimal TaxAmount
    {
        get => _taxAmount;
        set
        {
            var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
            if (_taxAmount == rounded) return;
            _taxAmount = rounded;
            OnPropertyChanged();
        }
    }

    private bool _isLength;
    public bool IsLength
    {
        get => _isLength;
        set
        {
            if (_isLength == value) return;
            _isLength = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LineTotal));
            OnPropertyChanged(nameof(ExtendedPrice));
            OnPropertyChanged(nameof(DisplayQtyLine));
            OnPropertyChanged(nameof(LengthPreviewLine));
            OnPropertyChanged(nameof(QuantityValue));
        }
    }

    private decimal _qty = 1m;
    public decimal Qty
    {
        get => _qty;
        set
        {
            if (_qty == value) return;
            _qty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LineTotal));
            OnPropertyChanged(nameof(ExtendedPrice));
            OnPropertyChanged(nameof(DisplayQtyLine));
            OnPropertyChanged(nameof(QuantityValue));
        }
    }

    private int _qtyInches = 1;
    public int QtyInches
    {
        get => _qtyInches;
        set
        {
            if (_qtyInches == value) return;
            _qtyInches = value;
            SyncFeetInchesTextFromQtyInches();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LineTotal));
            OnPropertyChanged(nameof(ExtendedPrice));
            OnPropertyChanged(nameof(DisplayQtyLine));
            OnPropertyChanged(nameof(LengthPreviewLine));
            OnPropertyChanged(nameof(InchesText));
            OnPropertyChanged(nameof(QuantityValue));
        }
    }

    public decimal QuantityValue => IsLength ? QtyInches : Qty;
    public decimal ExtendedPrice => LineTotal;
    private string _feetText = "0";
    public string FeetText
    {
        get => _feetText;
        set
        {
            if (_feetText == value) return;
            _feetText = value;
            OnPropertyChanged();
            TryUpdateQtyInchesFromFeetInches();
        }
    }

    private string _inchesText = "1";
    public string InchesText
    {
        get => _inchesText;
        set
        {
            if (_inchesText == value) return;
            _inchesText = value;
            OnPropertyChanged();
            TryUpdateQtyInchesFromFeetInches();
        }
    }

    private string _inchesOnlyText = "1";
    public string InchesOnlyText
    {
        get => _inchesOnlyText;
        set
        {
            if (_inchesOnlyText == value) return;
            _inchesOnlyText = value;
            OnPropertyChanged();
            TryUpdateQtyInchesFromInchesOnly();
        }
    }

    public string DisplayQtyLine
    {
        get
        {
            if (!IsLength) return $"Qty: {Qty:0.###}";
            var fi = LengthConverter.FromTotalInches(Math.Max(0, QtyInches));
            return $"Qty: {fi.Feet} ft {fi.Inches} in  ({QtyInches} in)";
        }
    }

    public string LengthPreviewLine
    {
        get
        {
            if (!IsLength) return "";
            var fi = LengthConverter.FromTotalInches(Math.Max(0, QtyInches));
            return $"Selling length: {fi.Feet} ft {fi.Inches} in";
        }
    }

    public decimal LineTotal
    {
        get
        {
            if (!IsLength) return Money(UnitPrice * Qty);
            return Money(UnitPrice * QtyInches);
        }
    }

    private static decimal Money(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static bool TryParseNonNegInt(string text, out int value)
    {
        text = (text ?? "").Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return false;
        return value >= 0;
    }

    private static bool TryParseDecimal(string text, out decimal value)
    {
        text = (text ?? "").Trim();
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    public void BumpUnit(int delta)
    {
        if (IsLength) return;
        var next = Qty + delta;
        if (next < 0) next = 0;
        Qty = next;
    }

    public void BumpInches(int delta)
    {
        if (!IsLength) return;
        var next = QtyInches + delta;
        if (next < 0) next = 0;
        QtyInches = next;
    }

    public void ApplyUnitQtyText(string qtyText)
    {
        if (TryParseDecimal(qtyText, out var q) && q >= 0)
            Qty = q;
    }

    private void TryUpdateQtyInchesFromFeetInches()
    {
        if (!IsLength) return;
        if (!TryParseNonNegInt(FeetText, out var ft)) return;
        if (!TryParseNonNegInt(InchesText, out var inch)) return;

        var norm = LengthConverter.Normalize(ft, inch);
        var total = LengthConverter.ToTotalInches(norm.Feet, norm.Inches);

        if (total != QtyInches)
        {
            _qtyInches = total;
            _inchesOnlyText = total.ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(QtyInches));
            OnPropertyChanged(nameof(LineTotal));
            OnPropertyChanged(nameof(ExtendedPrice));
            OnPropertyChanged(nameof(DisplayQtyLine));
            OnPropertyChanged(nameof(LengthPreviewLine));
            OnPropertyChanged(nameof(InchesOnlyText));
            OnPropertyChanged(nameof(QuantityValue));
        }
    }

    private void TryUpdateQtyInchesFromInchesOnly()
    {
        if (!IsLength) return;
        if (!TryParseNonNegInt(InchesOnlyText, out var inches)) return;

        if (inches != QtyInches)
            QtyInches = inches;
    }

    private void SyncFeetInchesTextFromQtyInches()
    {
        if (!IsLength) return;

        var fi = LengthConverter.FromTotalInches(Math.Max(0, QtyInches));
        _feetText = fi.Feet.ToString(CultureInfo.InvariantCulture);
        _inchesText = fi.Inches.ToString(CultureInfo.InvariantCulture);
        _inchesOnlyText = Math.Max(0, QtyInches).ToString(CultureInfo.InvariantCulture);

        OnPropertyChanged(nameof(FeetText));
        OnPropertyChanged(nameof(InchesText));
        OnPropertyChanged(nameof(InchesOnlyText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
