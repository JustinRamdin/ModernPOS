using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Linq;

using Pos.Terminal.Models;

namespace Pos.Terminal.Services;

public static class PhysicalReceiptRenderer
{
    public sealed class InvoicePrintState
    {
        public int ItemIndex;
        public List<(string desc, decimal amount)> Items { get; init; } = new();
    }

    public sealed record ReceiptCustomerInfo(string Name, string Phone, string Email);

    public sealed record ReceiptRenderLine(string Name, bool IsLength, decimal Qty, int QtyInches, decimal UnitPrice, decimal LineTotal);

    public static InvoicePrintState CreateState(IEnumerable<ReceiptRenderLine> lines)
    {
        return new InvoicePrintState
        {
            ItemIndex = 0,
            Items = lines.Select(line =>
            {
                var qtyLabel = line.IsLength ? $"{line.QtyInches:0.##} in" : $"{line.Qty:0.##}";
                var desc = $"{line.Name}\nQty: {qtyLabel}  @  {line.UnitPrice:0.00}";
                return (desc, line.LineTotal);
            }).ToList()
        };
    }

    public static bool DrawInvoiceLetterPage(
        Graphics g,
        Rectangle pageBounds,
        Margins margins,
        AppSettings settings,
        string receiptNo,
        DateTime invoiceDate,
        ReceiptCustomerInfo customer,
        string paymentMethod,
        decimal total,
        decimal cashGiven,
        decimal change,
        InvoicePrintState state)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        float dpiX = g.DpiX;
        float dpiY = g.DpiY;

        float left = (margins.Left / 100f) * dpiX;
        float right = (margins.Right / 100f) * dpiX;
        float top = (margins.Top / 100f) * dpiY;
        float bottom = (margins.Bottom / 100f) * dpiY;

        float pageW = pageBounds.Width;
        float pageH = pageBounds.Height;

        var content = new RectangleF(
            x: left,
            y: top,
            width: pageW - left - right,
            height: pageH - top - bottom
        );

        using var fontCompany = new Font("Segoe UI", 10f, FontStyle.Regular);
        using var fontSmall = new Font("Segoe UI", 9f, FontStyle.Regular);
        using var fontSmallBold = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var fontTitle = new Font("Segoe UI", 22f, FontStyle.Bold);
        using var fontSection = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var fontTableHeader = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var fontTable = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        using var pen = new Pen(Color.Gray, 1f);
        using var penDark = new Pen(Color.DimGray, 1f);
        using var brushHeaderFill = new SolidBrush(Color.FromArgb(220, 230, 245));

        static string Safe(string? s) => string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim();

        var sfNearTop = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };
        var sfFarTop = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };

        float y = content.Top;

        float headerH = 110f;
        var headerRect = new RectangleF(content.Left, y, content.Width, headerH);

        var companyRect = new RectangleF(headerRect.Left, headerRect.Top, headerRect.Width * 0.55f, headerRect.Height);
        var companyText =
            $"{Safe(settings.CompanyName)}\n" +
            $"{Safe(settings.CompanyAddress)}\n" +
            $"{Safe(settings.CompanyContact)}";
        g.DrawString(companyText, fontCompany, Brushes.Black, companyRect);

        var titleRect = new RectangleF(headerRect.Left + headerRect.Width * 0.55f, headerRect.Top, headerRect.Width * 0.45f, 40f);
        var sfRight = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };
        g.DrawString("INVOICE", fontTitle, new SolidBrush(Color.FromArgb(95, 135, 200)), titleRect, sfRight);

        float boxW = headerRect.Width * 0.45f;
        float boxX = headerRect.Right - boxW;
        float boxY = headerRect.Top + 50f;
        float boxH = 48f;

        var infoBox = new RectangleF(boxX + (boxW * 0.35f), boxY, boxW * 0.65f, boxH);
        g.DrawRectangle(penDark, infoBox.X, infoBox.Y, infoBox.Width, infoBox.Height);

        float colW = infoBox.Width / 2f;
        float rowH = infoBox.Height / 2f;

        g.FillRectangle(brushHeaderFill, infoBox.X, infoBox.Y, colW, rowH);
        g.FillRectangle(brushHeaderFill, infoBox.X + colW, infoBox.Y, colW, rowH);

        g.DrawLine(penDark, infoBox.X + colW, infoBox.Y, infoBox.X + colW, infoBox.Bottom);
        g.DrawLine(penDark, infoBox.X, infoBox.Y + rowH, infoBox.Right, infoBox.Y + rowH);

        var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        g.DrawString("INVOICE #", fontSmallBold, Brushes.Black, new RectangleF(infoBox.X, infoBox.Y, colW, rowH), sfCenter);
        g.DrawString("DATE", fontSmallBold, Brushes.Black, new RectangleF(infoBox.X + colW, infoBox.Y, colW, rowH), sfCenter);

        g.DrawString(receiptNo, fontSmall, Brushes.Black, new RectangleF(infoBox.X, infoBox.Y + rowH, colW, rowH), sfCenter);
        g.DrawString(invoiceDate.ToString("yyyy-MM-dd"), fontSmall, Brushes.Black, new RectangleF(infoBox.X + colW, infoBox.Y + rowH, colW, rowH), sfCenter);

        y += headerH + 10f;

        float billToW = content.Width * 0.45f;
        float billToH = 95f;

        var billToRect = new RectangleF(content.Left, y, billToW, billToH);
        g.DrawRectangle(penDark, billToRect.X, billToRect.Y, billToRect.Width, billToRect.Height);

        var billToHeader = new RectangleF(billToRect.X, billToRect.Y, billToRect.Width, 18f);
        g.FillRectangle(brushHeaderFill, billToHeader);
        g.DrawRectangle(penDark, billToHeader.X, billToHeader.Y, billToHeader.Width, billToHeader.Height);
        g.DrawString(
            "BILL TO",
            fontSection,
            Brushes.Black,
            new RectangleF(billToHeader.X + 6, billToHeader.Y, billToHeader.Width - 12, billToHeader.Height),
            new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center });

        var billTextRect = new RectangleF(billToRect.X + 8, billToRect.Y + 24, billToRect.Width - 16, billToRect.Height - 28);
        var billText =
            $"{Safe(customer.Name)}\n" +
            $"{Safe(customer.Phone)}\n" +
            $"{Safe(customer.Email)}";
        g.DrawString(billText, fontSmall, Brushes.Black, billTextRect);

        y += billToH + 12f;

        float tableX = content.Left;
        float tableW = content.Width;
        float tableTop = y;

        float rowHeight = 20f;
        float headerHeight = 22f;

        float descW = tableW * 0.78f;
        float amtW = tableW - descW;

        var hdrRect = new RectangleF(tableX, tableTop, tableW, headerHeight);
        g.DrawRectangle(penDark, hdrRect.X, hdrRect.Y, hdrRect.Width, hdrRect.Height);
        g.FillRectangle(brushHeaderFill, hdrRect.X, hdrRect.Y, hdrRect.Width, hdrRect.Height);
        g.DrawLine(penDark, tableX + descW, tableTop, tableX + descW, tableTop + headerHeight);

        g.DrawString(
           "DESCRIPTION",
           fontTableHeader,
           Brushes.Black,
           new RectangleF(tableX + 6, tableTop, descW - 12, headerHeight),
           new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center });

        g.DrawString(
           "AMOUNT",
           fontTableHeader,
           Brushes.Black,
           new RectangleF(tableX + descW + 6, tableTop, amtW - 12, headerHeight),
           new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });

        float bodyTop = tableTop + headerHeight;
        float bodyH = content.Bottom - bodyTop - 90f;
        var bodyRect = new RectangleF(tableX, bodyTop, tableW, bodyH);
        g.DrawRectangle(penDark, bodyRect.X, bodyRect.Y, bodyRect.Width, bodyRect.Height);

        float curY = bodyTop;
        int startIndex = state.ItemIndex;

        while (state.ItemIndex < state.Items.Count)
        {
            var (desc, amount) = state.Items[state.ItemIndex];
            int lines = 1 + desc.Count(c => c == '\n');
            float thisRowH = Math.Max(rowHeight, lines * 16f);

            if (curY + thisRowH > bodyRect.Bottom - 4)
                break;

            g.DrawLine(pen, tableX, curY + thisRowH, tableX + tableW, curY + thisRowH);
            g.DrawLine(pen, tableX + descW, curY, tableX + descW, curY + thisRowH);

            var descRect = new RectangleF(tableX + 6, curY + 3, descW - 12, thisRowH - 6);
            var amtRect = new RectangleF(tableX + descW + 6, curY + 3, amtW - 12, thisRowH - 6);

            g.DrawString(desc, fontTable, Brushes.Black, descRect, sfNearTop);
            g.DrawString(amount.ToString("0.00", CultureInfo.CurrentCulture), fontTable, Brushes.Black, amtRect, sfFarTop);

            curY += thisRowH;
            state.ItemIndex++;
        }

        float footerTop = content.Bottom - 78f;

        g.DrawString(
            "Thank you for your business!",
            fontSmall,
            Brushes.Black,
            new RectangleF(content.Left, footerTop, content.Width * 0.65f, 22f),
            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

        float totalBoxW = content.Width * 0.35f;
        float totalBoxH = 34f;
        var totalRect = new RectangleF(content.Right - totalBoxW, footerTop, totalBoxW, totalBoxH);
        g.DrawRectangle(penDark, totalRect.X, totalRect.Y, totalRect.Width, totalRect.Height);

        float totalLabelW = totalBoxW * 0.45f;
        g.DrawLine(penDark, totalRect.X + totalLabelW, totalRect.Y, totalRect.X + totalLabelW, totalRect.Bottom);

        g.DrawString(
            "TOTAL",
            fontSmallBold,
            Brushes.Black,
            new RectangleF(totalRect.X + 6, totalRect.Y, totalLabelW - 12, totalBoxH),
            new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center });

        g.DrawString(
            total.ToString("C", CultureInfo.CurrentCulture),
            fontSmallBold,
            Brushes.Black,
            new RectangleF(totalRect.X + totalLabelW + 6, totalRect.Y, totalBoxW - totalLabelW - 12, totalBoxH),
            new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });

        var contactLine = $"If you have any questions about this invoice, please contact {Safe(settings.CompanyContact)}";
        g.DrawString(
            contactLine,
            fontSmall,
            Brushes.DimGray,
            new RectangleF(content.Left, content.Bottom - 26f, content.Width, 18f),
            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

        var payLine = paymentMethod.Equals("CASH", StringComparison.OrdinalIgnoreCase)
            ? $"Payment: CASH   Cash: {cashGiven:0.00}   Change: {change:0.00}"
            : $"Payment: {paymentMethod}";
        g.DrawString(
            payLine,
            fontSmall,
            Brushes.DimGray,
            new RectangleF(content.Left, content.Bottom - 46f, content.Width, 18f),
            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

        bool hasMore = state.ItemIndex < state.Items.Count;

        if (hasMore && state.ItemIndex == startIndex)
            state.ItemIndex = Math.Min(state.Items.Count, startIndex + 1);

        return hasMore;
    }
}
