using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Checkout;
using Pos.Application.Inventory;
using Pos.Application.Tax;
using Pos.Local.Data;
using Pos.Local.Entities;

// aliases for enum mapping (mirrors LocalSaleService.cs style)
using AppLineKind = Pos.Application.Checkout.LineQuantityKind;
using LocalLineKind = Pos.Local.Entities.LineQuantityKind;

namespace Pos.Local.Services;

public class LocalSaleService
{
    private readonly PosLocalDbContext _db;
    private readonly CheckoutCalculator _checkout;

    public LocalSaleService(PosLocalDbContext db)
    {
        _db = db;
        _checkout = new CheckoutCalculator(new VatCalculator());
    }

    private static decimal Money(decimal v)
        => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public async Task<LocalSaleResult> CreateCashSaleAsync(
        string terminalId,
        IReadOnlyList<LocalCartLine> lines,
        decimal cashGiven,
        decimal discountAmount = 0m,
        Guid? customerId = null,
        string locationCode = "DEFAULT",
        bool allowNegativeStock = false,
        CancellationToken ct = default)
    {
        if (lines.Count == 0)
            throw new InvalidOperationException("Cart is empty.");

        var productIds = lines.Select(x => x.ProductId).Distinct().ToList();

        var products = await _db.Products
            .AsNoTracking()
            .Where(p =>
                productIds.Contains(p.Id) &&
                p.DeletedAtUtc == null &&
                p.IsActive)
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var l in lines)
        {
            if (!products.ContainsKey(l.ProductId))
                throw new InvalidOperationException($"Product missing from offline catalog: {l.ProductId}");

            if (l.QuantityKind == LocalLineKind.Unit && l.Qty <= 0)
                throw new InvalidOperationException("Invalid unit quantity.");

            if (l.QuantityKind == LocalLineKind.Inches && l.QtyInches <= 0)
                throw new InvalidOperationException("Invalid inches quantity.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var saleLines = new List<SaleLine>();

        // -----------------------------
        // BUILD SALE LINES (STABLE VAT)
        // -----------------------------
        foreach (var l in lines)
        {
            var p = products[l.ProductId];

            if (p.IsLength && l.QuantityKind != LocalLineKind.Inches)
                throw new InvalidOperationException($"{p.Name} must be sold by length.");

            if (!p.IsLength && l.QuantityKind != LocalLineKind.Unit)
                throw new InvalidOperationException($"{p.Name} must be sold by quantity.");

            // MAP Local enum → Application enum
            var appKind = l.QuantityKind == LocalLineKind.Inches
                ? AppLineKind.Inches
                : AppLineKind.Unit;

            var calc = _checkout.CalculateLine(
                productId: p.Id,
                productName: p.Name,
                enteredSellingPrice: p.Price, // price per unit OR per inch
                vatInclusive: p.VatInclusive,
                quantityKind: appKind,
                qty: l.Qty,
                qtyInches: l.QtyInches
            );

            saleLines.Add(new SaleLine
            {
                ProductId = p.Id,
                QuantityKind = l.QuantityKind,

                Qty = l.Qty,
                QtyInches = l.QtyInches,

                UnitNet = calc.UnitNet,
                UnitVat = calc.UnitVat,
                UnitGross = calc.UnitGross,

                NetTotal = calc.NetTotal,
                VatTotal = calc.VatTotal,
                GrossTotal = calc.GrossTotal
            });
        }

        var totals = _checkout.SumTotals(
            saleLines.Select(sl => new CheckoutLineTotals(
                sl.ProductId,
                "", // productName not stored on SaleLine entity
                sl.QuantityKind == LocalLineKind.Inches ? AppLineKind.Inches : AppLineKind.Unit,
                sl.Qty,
                sl.QtyInches,
                sl.UnitNet,
                sl.UnitVat,
                sl.UnitGross,
                sl.NetTotal,
                sl.VatTotal,
                sl.GrossTotal
            ))
        );

        var discount = Money(Math.Max(0m, discountAmount));
        var totalDue = Money(Math.Max(0m, totals.Gross - discount));

         if (cashGiven < totalDue)
            throw new InvalidOperationException($"Insufficient cash. Total is {totalDue:0.00}");

        var change = Money(cashGiven - totalDue);


        var receiptNo =
            $"{terminalId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32];

        var sale = new Sale
        {
            ReceiptNo = receiptNo,
            CustomerId = customerId,
            NetTotal = totals.Net,
            VatTotal = totals.Vat,
            GrossTotal = totalDue,
            Status = "Paid",
            CreatedAtUtc = DateTime.UtcNow,
            Lines = saleLines
        };

        _db.Sales.Add(sale);

        // -----------------------------
        // INVENTORY ADJUSTMENT
        // -----------------------------
        foreach (var l in lines)
        {
            var p = products[l.ProductId];

            var inv = await _db.Inventory.FirstOrDefaultAsync(
                x => x.ProductId == l.ProductId &&
                     x.LocationCode == locationCode,
                ct);

            if (inv == null)
            {
                inv = new InventoryBalance
                {
                    ProductId = l.ProductId,
                    LocationCode = locationCode,
                    OnHand = 0m,
                    OnHandInches = 0
                };
                _db.Inventory.Add(inv);
            }

            if (l.QuantityKind == LocalLineKind.Unit)
            {
                var res = StockService.TrySubtractUnits(
                    inv.OnHand,
                    l.Qty,
                    allowNegativeStock,
                    out var newQty);

                if (!res.Success)
                    throw new InvalidOperationException($"{p.Name}: {res.ErrorMessage}");

                inv.OnHand = Math.Round(newQty, 3);
            }
            else
            {
                var res = StockService.TrySubtractInches(
                    inv.OnHandInches,
                    l.QtyInches,
                    allowNegativeStock,
                    out var newInches);

                if (!res.Success)
                    throw new InvalidOperationException($"{p.Name}: {res.ErrorMessage}");

                inv.OnHandInches = newInches;
            }
        }

        await _db.SaveChangesAsync(ct);

        // -----------------------------
        // OUTBOX EVENT (SYNC-SAFE)
        // -----------------------------
        var payload = new
        {
            sale = new
            {
                id = sale.Id,
                receipt_no = sale.ReceiptNo,
                terminal_id = terminalId,
                net_total = sale.NetTotal,
                vat_total = sale.VatTotal,
                gross_total = sale.GrossTotal,
                discount_amount = discount,
                created_at_utc = sale.CreatedAtUtc,
                customer_id = sale.CustomerId
            },
            lines = sale.Lines.Select(sl => new
            {
                product_id = sl.ProductId,
                quantity_kind = (int)sl.QuantityKind,
                qty = sl.Qty,
                qty_inches = sl.QtyInches,
                unit_net = sl.UnitNet,
                unit_vat = sl.UnitVat,
                unit_gross = sl.UnitGross,
                net_total = sl.NetTotal,
                vat_total = sl.VatTotal,
                gross_total = sl.GrossTotal
            }),
            payment = new
            {
                method = "CASH",
                cash_given = cashGiven,
                change = change
            }
        };

        _db.Outbox.Add(new OutboxEvent
        {
            EntityType = "sale",
            EntityId = sale.Id,
            Operation = "UPSERT",
            PayloadJson = JsonSerializer.Serialize(payload)
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new LocalSaleResult
        {
            SaleId = sale.Id,
            ReceiptNo = sale.ReceiptNo,
            Total = sale.GrossTotal,
            CashGiven = cashGiven,
            Change = change
        };
    }

    // ✅ NEW: Card checkout (Debit/Credit)
    public async Task<LocalSaleResult> CreateCardSaleAsync(
        string terminalId,
        IReadOnlyList<LocalCartLine> lines,
        string method, // "DEBIT" or "CREDIT"
        decimal discountAmount = 0m,
        Guid? customerId = null,
        string locationCode = "DEFAULT",
        bool allowNegativeStock = false,
        CancellationToken ct = default)
    {
        // Reuse stable VAT + inventory logic; bypass cash validation by giving MaxValue.
        var result = await CreateCashSaleAsync(
            terminalId: terminalId,
            lines: lines,
            cashGiven: decimal.MaxValue,
            discountAmount: discountAmount,
            customerId: customerId,
            locationCode: locationCode,
            allowNegativeStock: allowNegativeStock,
            ct: ct);

        // Patch latest outbox for this sale so payment method matches, and cash_given/change are sane.
        var evt = await _db.Outbox
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(x => x.EntityType == "sale" && x.EntityId == result.SaleId, ct);

       if (evt != null)
            TryUpdatePaymentPayload(evt, method, result.Total, 0m);

        await _db.SaveChangesAsync(ct);

        return new LocalSaleResult
        {
            SaleId = result.SaleId,
            ReceiptNo = result.ReceiptNo,
            Total = result.Total,
            CashGiven = result.Total,
            Change = 0m
        };
    }

    public async Task<LocalSaleResult> CreateOnAccountSaleAsync(
        string terminalId,
        IReadOnlyList<LocalCartLine> lines,
        Guid customerId,
        decimal discountAmount = 0m,
        string locationCode = "DEFAULT",
        bool allowNegativeStock = false,
        CancellationToken ct = default)
    {
        // Stable VAT totals + adjust inventory; bypass cash checks.
        var result = await CreateCashSaleAsync(
            terminalId: terminalId,
            lines: lines,
            cashGiven: decimal.MaxValue,
            discountAmount: discountAmount,
            customerId: customerId,
            locationCode: locationCode,
            allowNegativeStock: allowNegativeStock,
            ct: ct);

        // Mark sale as OnAccount
        var sale = await _db.Sales.FirstAsync(s => s.Id == result.SaleId, ct);
        sale.Status = "OnAccount";

        // ✅ Update stored customer balance
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer != null)
        {
            customer.Balance = Math.Round(customer.Balance + sale.GrossTotal, 2, MidpointRounding.AwayFromZero);
        }


        // Patch outbox payment method
        var evt = await _db.Outbox
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(x => x.EntityType == "sale" && x.EntityId == sale.Id, ct);

       if (evt != null)
            TryUpdatePaymentPayload(evt, "ON_ACCOUNT", 0m, 0m);

        await _db.SaveChangesAsync(ct);

        return result;
    }

    public async Task<decimal> GetCustomerBalanceAsync(Guid customerId, CancellationToken ct = default)
    {
        // Balance = sum(OnAccount sales gross) - sum(payments)
        var onAccountTotal = await _db.Sales
            .Where(s => s.CustomerId == customerId && s.Status == "OnAccount")
            .SumAsync(s => (decimal?)s.GrossTotal, ct) ?? 0m;

        var paidTotal = await _db.CustomerPayments
            .Where(p => p.CustomerId == customerId)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        return onAccountTotal - paidTotal;
    }

    public async Task<Guid> AddCustomerPaymentAsync(
        Guid customerId,
        decimal amount,
        string method,
        string? referenceNo = null,
        string? note = null,
        CancellationToken ct = default)
    {
        if (amount <= 0) throw new InvalidOperationException("Payment amount must be greater than 0.");

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer != null)
        {
            customer.Balance = Math.Round(customer.Balance - amount, 2, MidpointRounding.AwayFromZero);
        }

        var payment = new Pos.Local.Entities.CustomerPayment
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Amount = amount,
            Method = method,
            ReferenceNo = referenceNo,
            Note = note,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.CustomerPayments.Add(payment);

        // Optional: write an outbox event so it syncs later (recommended if you sync)
        _db.Outbox.Add(new OutboxEvent
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            EntityType = "customer_payment",
            EntityId = payment.Id,
            PayloadJson = $$"""
            {
              "id":"{{payment.Id}}",
              "customerId":"{{payment.CustomerId}}",
              "amount":{{payment.Amount}},
              "method":"{{payment.Method}}",
              "referenceNo":{{(referenceNo is null ? "null" : "\""+referenceNo+"\"")}},
              "createdAtUtc":"{{payment.CreatedAtUtc:O}}"
            }
            """
        });

        await _db.SaveChangesAsync(ct);
        return payment.Id;
    }
      private static bool TryUpdatePaymentPayload(OutboxEvent evt, string method, decimal cashGiven, decimal change)
    {
        if (string.IsNullOrWhiteSpace(evt.PayloadJson))
            return false;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(evt.PayloadJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (node is not JsonObject root || root["payment"] is not JsonObject payment)
            return false;

        payment["method"] = method;
        payment["cash_given"] = cashGiven;
        payment["change"] = change;

        evt.PayloadJson = root.ToJsonString();
        return true;
    }
}

// =====================================================
// DTOs
// =====================================================

public sealed class LocalCartLine
{
    public Guid ProductId { get; init; }

    // ALWAYS use Local enum here
    public LocalLineKind QuantityKind { get; init; } = LocalLineKind.Unit;

    public decimal Qty { get; init; }
    public int QtyInches { get; init; }
}

public sealed class LocalSaleResult
{
    public Guid SaleId { get; init; }
    public string ReceiptNo { get; init; } = "";
    public decimal Total { get; init; }
    public decimal CashGiven { get; init; }
    public decimal Change { get; init; }
}
