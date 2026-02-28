using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Local.Data;
using DataLocalDb = Pos.Local.Data.LocalDb;
using Pos.Local.Entities;
using Pos.Terminal.Models;
using Pos.Terminal.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Container = QuestPDF.Infrastructure.IContainer;

namespace Pos.Terminal.ViewModels;

public sealed class FinancialViewModel : INotifyPropertyChanged
{
    public FinancialDocumentEditorViewModel Quote { get; } = new("Quote", "Q");
    public FinancialDocumentEditorViewModel Invoice { get; } = new("Invoice", "INV");

    public ObservableCollection<CustomerChoice> Customers { get; } = new();
    public ObservableCollection<ProductChoice> Products { get; } = new();

    private string _status = "Ready";
    public string Status
    {
        get => _status;
        set { _status = value; Raise(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync()
    {
        try
        {
            Status = "Loading financial data...";
            await using var db = new PosLocalDbContext(BuildDbOptions());
            await db.Database.EnsureCreatedAsync();

            var customers = await db.Customers.AsNoTracking()
                .Where(c => c.DeletedAtUtc == null)
                .OrderBy(c => c.Name)
                .ToListAsync();

            var products = await db.Products.AsNoTracking()
                .Where(p => p.IsActive && p.DeletedAtUtc == null)
                .OrderBy(p => p.Name)
                .ToListAsync();

            Customers.Clear();
            foreach (var c in customers)
                Customers.Add(new CustomerChoice(c.Id, c.Name, c.Phone, c.Email));

            Products.Clear();
            foreach (var p in products)
                Products.Add(new ProductChoice(p.Id, p.Sku, p.Name, p.Price));

            Quote.BindReferences(Customers, Products);
            Invoice.BindReferences(Customers, Products);

            Status = "Financial workspace ready.";
        }
        catch (Exception ex)
        {
            Status = $"Failed to load financial data: {ex.Message}";
        }
    }

    public void AddLine(FinancialDocumentEditorViewModel editor)
    {
        var product = Products.FirstOrDefault(p => p.Id == editor.SelectedProductId);
        if (product == null)
        {
            Status = "Select an inventory item first.";
            return;
        }

        var qty = editor.LineQuantity <= 0 ? 1m : editor.LineQuantity;
        var unitPrice = editor.LineUnitPrice <= 0 ? product.Price : editor.LineUnitPrice;

        editor.Lines.Add(new FinancialLineItem(product.Id, product.DisplayName, qty, unitPrice, editor.RecalculateTotals));
        editor.LineQuantity = 1m;
        editor.LineUnitPrice = product.Price;
        editor.RecalculateTotals();
        Status = $"Added {product.Name}.";
    }

    public void RemoveSelectedLine(FinancialDocumentEditorViewModel editor)
    {
        if (editor.SelectedLine == null)
        {
            Status = "Select a line to remove.";
            return;
        }

        editor.Lines.Remove(editor.SelectedLine);
        editor.RecalculateTotals();
        Status = "Line removed.";
    }

    public async Task SavePdfAsync(FinancialDocumentEditorViewModel editor, string filePath)
    {
        if (!ValidateDocument(editor, out var customer)) return;

        var settings = await new SettingsStore().LoadAsync();
        await Task.Run(() => BuildPdf(editor, customer!, settings, filePath));
        Status = $"PDF saved: {filePath}";
    }

    public void Print(FinancialDocumentEditorViewModel editor)
    {
        if (!ValidateDocument(editor, out _)) return;

        Status = "Printing is available through your OS print dialog after opening the generated PDF.";
    }

    private bool ValidateDocument(FinancialDocumentEditorViewModel editor, out CustomerChoice? customer)
    {
        customer = Customers.FirstOrDefault(c => c.Id == editor.SelectedCustomerId);
        if (customer == null)
        {
            Status = $"Select a customer for the {editor.DocumentType.ToLowerInvariant()}.";
            return false;
        }

        if (editor.Lines.Count == 0)
        {
            Status = $"Add at least one item to the {editor.DocumentType.ToLowerInvariant()}.";
            return false;
        }

        return true;
    }

    private static void BuildPdf(FinancialDocumentEditorViewModel editor, CustomerChoice customer, AppSettings settings, string filePath)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                      col.Item().Background("#E35A2C").Height(10);

                    col.Item().Background(Colors.Grey.Lighten3).Padding(18).Row(row =>
                    {
                        row.RelativeItem(6).Row(left =>
                        {
                            left.ConstantItem(68).Height(68).AlignMiddle().AlignCenter().Element(box =>
                            {
                                if (TryLoadImageBytes(settings.LogoImagePath, out var logoBytes))
                                    box.Image(logoBytes).FitArea();
                                else
                                    box.Border(1).BorderColor(Colors.Grey.Lighten1).AlignCenter().AlignMiddle().Text("LOGO").FontSize(9);
                            });

                            left.RelativeItem().PaddingLeft(12).Column(company =>
                            {
                                company.Spacing(3);
                                company.Item().Text(string.IsNullOrWhiteSpace(settings.CompanyName) ? "<Your Company Name>" : settings.CompanyName).Bold();
                                company.Item().Text(string.IsNullOrWhiteSpace(settings.CompanyAddress) ? "<Your address>" : settings.CompanyAddress);
                                company.Item().Text(string.IsNullOrWhiteSpace(settings.CompanyContact) ? "<Your contact details>" : settings.CompanyContact);
                            });
                        });

                        row.RelativeItem(4).AlignRight().Column(meta =>
                        {
                            meta.Item().Text(editor.DocumentType.ToUpperInvariant() == "QUOTE" ? "PRICE QUOTE" : editor.DocumentType.ToUpperInvariant())
                                .FontSize(24).FontColor(Colors.Grey.Darken2).SemiBold();
                            meta.Item().PaddingTop(10).AlignRight().Width(180).Column(c =>
                            {
                                c.Item().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(4).Row(r =>
                                {
                                    r.RelativeItem().AlignRight().Text("DATE").SemiBold();
                                    r.ConstantItem(90).AlignRight().Text($"{editor.IssueDate:yyyy-MM-dd}");
                                });
                                c.Item().PaddingTop(8).BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(4).Row(r =>
                                {
                                    r.RelativeItem().AlignRight().Text("QUOTE NO.").SemiBold();
                                    r.ConstantItem(90).AlignRight().Text(editor.DocumentNumber);
                                });
                            });
                        });
                    });
                });

                page.Content().Column(col =>
                {
                    col.Spacing(14);
                    col.Item().PaddingTop(10).Row(info =>
                    {
                         info.RelativeItem().Column(customerCol =>
                        {
                            customerCol.Item().Text("BILL TO").SemiBold();
                            customerCol.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                            customerCol.Item().PaddingTop(4).Text(customer.Name);
                            if (!string.IsNullOrWhiteSpace(customer.Phone)) customerCol.Item().Text(customer.Phone);
                            if (!string.IsNullOrWhiteSpace(customer.Email)) customerCol.Item().Text(customer.Email);
                        });

                        info.ConstantItem(30);

                        info.RelativeItem().Column(shipCol =>
                        {
                            shipCol.Item().Text("SHIP TO").SemiBold();
                            shipCol.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                            shipCol.Item().PaddingTop(4).Text(customer.Name);
                            if (!string.IsNullOrWhiteSpace(customer.Phone)) shipCol.Item().Text(customer.Phone);
                            if (!string.IsNullOrWhiteSpace(customer.Email)) shipCol.Item().Text(customer.Email);
                        });
                    });

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(4);
                            cols.RelativeColumn(1);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(2);
                        });
                        table.Header(header =>
                        {
                            header.Cell().Background("#E35A2C").Element(CellStyle).Text("DESCRIPTION").Bold().FontColor(Colors.White);
                            header.Cell().Background("#E35A2C").Element(CellStyle).AlignRight().Text("QTY").Bold().FontColor(Colors.White);
                            header.Cell().Background("#E35A2C").Element(CellStyle).AlignRight().Text("UNIT PRICE").Bold().FontColor(Colors.White);
                            header.Cell().Background("#E35A2C").Element(CellStyle).AlignRight().Text("TOTAL").Bold().FontColor(Colors.White);
                        });

                        foreach (var line in editor.Lines)
                        {
                            table.Cell().Element(BodyCellStyle).Text(line.Description);
                            table.Cell().Element(BodyCellStyle).AlignRight().Text(line.Quantity.ToString("0.##", CultureInfo.InvariantCulture));
                            table.Cell().Element(BodyCellStyle).AlignRight().Text(line.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture));
                            table.Cell().Element(BodyCellStyle).AlignRight().Text(line.LineTotal.ToString("0.00", CultureInfo.InvariantCulture));
                        }

                           static Container CellStyle(Container c)
                            => c.Border(1).BorderColor(Colors.White).PaddingVertical(5).PaddingHorizontal(6);
                    });

                    static Container BodyCellStyle(Container c)
                            => c.Border(1).BorderColor(Colors.Grey.Lighten2).Background(Colors.Grey.Lighten4).PaddingVertical(5).PaddingHorizontal(6);
                    });

                    col.Item().Row(r =>
                    {
                        r.RelativeItem().PaddingTop(8).Text(string.IsNullOrWhiteSpace(editor.Notes)
                            ? "Remarks, notes on estimate validity and project duration."
                            : editor.Notes);

                        r.ConstantItem(280).Column(sum =>
                        {
                           sum.Item().Row(x => { x.RelativeItem().AlignRight().Text("SUBTOTAL"); x.ConstantItem(90).AlignRight().Text(editor.Subtotal.ToString("0.00")); });
                            sum.Item().Row(x => { x.RelativeItem().AlignRight().Text("DISCOUNT"); x.ConstantItem(90).AlignRight().Text(editor.DiscountAmount.ToString("0.00")); });
                            sum.Item().Row(x => { x.RelativeItem().AlignRight().Text("SUBTOTAL LESS DISCOUNT"); x.ConstantItem(90).AlignRight().Text((editor.Subtotal - editor.DiscountAmount).ToString("0.00")); });
                            sum.Item().Row(x => { x.RelativeItem().AlignRight().Text($"TAX RATE"); x.ConstantItem(90).AlignRight().Text($"{editor.TaxRate:0.##}%"); });
                            sum.Item().Row(x => { x.RelativeItem().AlignRight().Text("TOTAL TAX"); x.ConstantItem(90).AlignRight().Text(editor.TaxAmount.ToString("0.00")); });
                            sum.Item().BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(4).Background("#DCE6D6").Padding(6).Row(x =>
                            {
                                x.RelativeItem().Text($"{editor.DocumentType} Total").Bold();
                                x.ConstantItem(90).AlignRight().Text(editor.Total.ToString("0.00")).Bold();
                            });
                        });
                    });
                });

                page.Footer().Background("#E35A2C").Height(10);
            });
        }).GeneratePdf(filePath);
    }

    private static bool TryLoadImageBytes(string? imagePath, out byte[] imageBytes)
    {
        imageBytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return false;

        try
        {
            imageBytes = File.ReadAllBytes(imagePath);
            return imageBytes.Length > 0;
        }
        catch
        {
            return false;
        }
    }
    
    private static DbContextOptions<PosLocalDbContext> BuildDbOptions()
   => DataLocalDb.BuildOptions();

    private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class FinancialDocumentEditorViewModel : INotifyPropertyChanged
{
    private IReadOnlyList<CustomerChoice> _customers = Array.Empty<CustomerChoice>();
    private IReadOnlyList<ProductChoice> _products = Array.Empty<ProductChoice>();

    private string _customerSearchText = "";
    public string CustomerSearchText
    {
        get => _customerSearchText;
        set
        {
            _customerSearchText = value ?? "";
            Raise();
            Raise(nameof(FilteredCustomers));
        }
    }

    private string _productSearchText = "";
    public string ProductSearchText
    {
        get => _productSearchText;
        set
        {
            _productSearchText = value ?? "";
            Raise();
            Raise(nameof(FilteredProducts));
        }
    }

    public IEnumerable<CustomerChoice> FilteredCustomers => string.IsNullOrWhiteSpace(CustomerSearchText)
        ? _customers
        : _customers.Where(c =>
            (c.Name ?? "").Contains(CustomerSearchText, StringComparison.OrdinalIgnoreCase)
            || (c.Phone ?? "").Contains(CustomerSearchText, StringComparison.OrdinalIgnoreCase)
            || (c.Email ?? "").Contains(CustomerSearchText, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<ProductChoice> FilteredProducts => string.IsNullOrWhiteSpace(ProductSearchText)
        ? _products
        : _products.Where(p =>
            (p.Name ?? "").Contains(ProductSearchText, StringComparison.OrdinalIgnoreCase)
            || (p.Sku ?? "").Contains(ProductSearchText, StringComparison.OrdinalIgnoreCase));

    public string DocumentType { get; }
    public ObservableCollection<FinancialLineItem> Lines { get; } = new();

    private string _documentNumber;
    public string DocumentNumber { get => _documentNumber; set { _documentNumber = value ?? ""; Raise(); } }

    private DateTimeOffset _issueDate = DateTimeOffset.Now.Date;
    public DateTimeOffset IssueDate { get => _issueDate; set { _issueDate = value; Raise(); } }

     private DateTimeOffset? _dueDate = DateTimeOffset.Now.Date.AddDays(14);
    public DateTimeOffset? DueDate { get => _dueDate; set { _dueDate = value; Raise(); } }

    private Guid? _selectedCustomerId;
    public Guid? SelectedCustomerId { get => _selectedCustomerId; set { _selectedCustomerId = value; Raise(); } }

    private Guid? _selectedProductId;
    public Guid? SelectedProductId
    {
        get => _selectedProductId;
        set
        {
            _selectedProductId = value;
            Raise();

            var product = _products.FirstOrDefault(p => p.Id == value);
            if (product != null) LineUnitPrice = product.Price;
        }
    }

    private decimal _lineQuantity = 1m;
    public decimal LineQuantity { get => _lineQuantity; set { _lineQuantity = value; Raise(); } }

    private decimal _lineUnitPrice;
    public decimal LineUnitPrice { get => _lineUnitPrice; set { _lineUnitPrice = value; Raise(); } }

    private decimal _taxRate = 0m;
    public decimal TaxRate { get => _taxRate; set { _taxRate = value; Raise(); RecalculateTotals(); } }

    private string _notes = "";
    public string Notes { get => _notes; set { _notes = value ?? ""; Raise(); } }

    private FinancialLineItem? _selectedLine;
    public FinancialLineItem? SelectedLine { get => _selectedLine; set { _selectedLine = value; Raise(); } }

    private decimal _subtotal;
    public decimal Subtotal { get => _subtotal; private set { _subtotal = value; Raise(); } }

    private decimal _taxAmount;
    public decimal TaxAmount { get => _taxAmount; private set { _taxAmount = value; Raise(); } }

    private decimal _total;
    public decimal Total { get => _total; private set { _total = value; Raise(); } }

    public FinancialDocumentEditorViewModel(string documentType, string prefix)
    {
        DocumentType = documentType;
        _documentNumber = $"{prefix}-{DateTime.Now:yyyyMMddHHmm}";

        Lines.CollectionChanged += (_, __) => RecalculateTotals();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void BindReferences(IReadOnlyList<CustomerChoice> customers, IReadOnlyList<ProductChoice> products)
    {
        _customers = customers;
        _products = products;
        Raise(nameof(FilteredCustomers));
        Raise(nameof(FilteredProducts));
        if (SelectedCustomerId == null && customers.Count > 0)
            SelectedCustomerId = customers[0].Id;

        if (SelectedProductId == null && products.Count > 0)
            SelectedProductId = products[0].Id;
    }

    public void RecalculateTotals()
    {
        Subtotal = Math.Round(Lines.Sum(x => x.LineTotal), 2);
        TaxAmount = Math.Round(Subtotal * (TaxRate / 100m), 2);
        Total = Math.Round(Subtotal + TaxAmount, 2);
    }

    private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class FinancialLineItem : INotifyPropertyChanged
{
    private readonly Action _notifyTotal;

    public Guid ProductId { get; }

    public string Description { get; }

    private decimal _quantity;
    public decimal Quantity
    {
        get => _quantity;
        set
        {
            _quantity = value;
            Raise();
            Raise(nameof(LineTotal));
            _notifyTotal();
        }
    }

    private decimal _unitPrice;
    public decimal UnitPrice
    {
        get => _unitPrice;
        set
        {
            _unitPrice = value;
            Raise();
            Raise(nameof(LineTotal));
            _notifyTotal();
        }
    }

    public decimal LineTotal => Math.Round(Quantity * UnitPrice, 2);

    public FinancialLineItem(Guid productId, string description, decimal quantity, decimal unitPrice, Action notifyTotal)
    {
        ProductId = productId;
        Description = description;
        _quantity = quantity;
        _unitPrice = unitPrice;
        _notifyTotal = notifyTotal;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed record CustomerChoice(Guid Id, string Name, string Phone, string Email)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Phone) ? Name : $"{Name} ({Phone})";
}

public sealed record ProductChoice(Guid Id, string Sku, string Name, decimal Price)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Sku) ? Name : $"{Sku} - {Name}";
}
