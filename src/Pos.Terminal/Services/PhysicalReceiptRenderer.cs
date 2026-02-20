using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;

using Pos.Terminal.Models;

namespace Pos.Terminal.Services;

public static class PhysicalReceiptRenderer
{
    public sealed class InvoicePrintState
    {
        public int ItemIndex;
        public List<(string description, string qty, decimal unitPrice, decimal amount)> Items { get; init; } = new();
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
                 return (line.Name, qtyLabel, line.UnitPrice, line.LineTotal);
            }).ToList()
        };
    }

    public static bool DrawInvoiceLetterPage(
        Graphics g,
        Rectangle marginBounds,
        AppSettings settings,
        string receiptNo,
        DateTime invoiceDate,
        ReceiptCustomerInfo customer,
        string paymentMethod,
         decimal subtotal,
        decimal discount,
        decimal vat,
        decimal totalDue,
        decimal totalTendered,
        decimal change,
        string remarks,
        InvoicePrintState state)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;


        var content = new RectangleF(
            x: marginBounds.Left,
            y: marginBounds.Top,
            width: marginBounds.Width,
            height: marginBounds.Height
        );

        using var fontBody = new Font("Segoe UI", 9f, FontStyle.Regular);
        using var fontSmall = new Font("Segoe UI", 8f, FontStyle.Regular);
        using var fontSmallBold = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var fontTitle = new Font("Segoe UI", 21f, FontStyle.Regular);
        using var fontLogo = new Font("Segoe UI", 15f, FontStyle.Regular);
        using var fontTableHeader = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var fontTable = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        using var fontPaid = new Font("Segoe UI", 12f, FontStyle.Bold);
        using var fontRemarks = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        using var penLight = new Pen(Color.FromArgb(180, 180, 180), 0.9f);
        using var penDark = new Pen(Color.FromArgb(95, 95, 95), 1f);
        using var penHeader = new Pen(Color.FromArgb(185, 0, 0), 1f);
        using var brushPage = new SolidBrush(Color.FromArgb(238, 238, 238));
        using var brushHeaderFill = new SolidBrush(Color.FromArgb(185, 0, 0));
        using var brushLogo = new SolidBrush(Color.FromArgb(90, 90, 90));

        static string Safe(string? s) => string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim();

        var sfNearTop = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };
        var sfFarTop = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };
         var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var sfFarCenter = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

        g.FillRectangle(brushPage, content);

        float y = content.Top + 4f;
        float stripeH = 12f;
        g.FillRectangle(brushHeaderFill, new RectangleF(content.Left, y, content.Width, stripeH));
        y += stripeH + 30f;

        g.DrawString("RECEIPT", fontTitle, Brushes.DimGray, new RectangleF(content.Left + 2f, y, content.Width * 0.45f, 40f));

        var logoMultiplier = Math.Clamp(settings.LogoScaleMultiplier, 1, 4);
        float logoSize = 76f * logoMultiplier;
        float logoX = content.Right - logoSize - 8f;
        float logoY = y - 60f;
       var logoRect = new RectangleF(logoX, logoY, logoSize, logoSize);

        if (TryLoadLogoImage(settings.LogoImagePath, out var logoImage))
        {
            using (logoImage)
            {
                g.DrawImage(logoImage, logoRect);
            }
        }
        else
        {
            g.FillEllipse(brushLogo, logoRect);
            g.DrawString("LOGO", fontLogo, Brushes.White, logoRect, sfCenter);
        }

        float metaX = content.Right - 100f;
        float metaY = logoY + logoSize + 12f;
        g.DrawString("PAYMENT DATE", fontSmallBold, Brushes.MidnightBlue, new RectangleF(metaX, metaY, 95f, 16f), sfFarCenter);
        g.DrawLine(penLight, metaX, metaY + 18f, metaX + 95f, metaY + 18f);
        g.DrawString(invoiceDate.ToString("yyyy-MM-dd"), fontSmall, Brushes.Black, new RectangleF(metaX, metaY + 20f, 95f, 14f), sfFarCenter);
        g.DrawString("RECEIPT NO.", fontSmallBold, Brushes.MidnightBlue, new RectangleF(metaX, metaY + 40f, 95f, 16f), sfFarCenter);
        g.DrawLine(penLight, metaX, metaY + 58f, metaX + 95f, metaY + 58f);
        g.DrawString(receiptNo, fontSmall, Brushes.Black, new RectangleF(metaX, metaY + 60f, 95f, 14f), sfFarCenter);

        var companyText =
            $"{Safe(settings.CompanyName)}\n" +
            $"{Safe(settings.CompanyAddress)}\n" +
            $"{Safe(settings.CompanyContact)}";
        g.DrawString(companyText, fontBody, Brushes.Black, new RectangleF(content.Left + 2f, y + 55f, content.Width * 0.5f, 84f));

        y += 165f;

       float leftColW = content.Width * 0.44f;
        float gap = 26f;
        float rightColX = content.Left + leftColW + gap;
        float infoH = 110f;

        g.DrawString("BILL TO", fontSmallBold, Brushes.MidnightBlue, new RectangleF(content.Left, y, leftColW, 14f));
        g.DrawLine(penLight, content.Left, y + 16f, content.Left + leftColW, y + 16f);
        var billText =
            $"{Safe(customer.Name)}\n" +
            $"{Safe(customer.Phone)}\n" +
            $"{Safe(customer.Email)}";
         g.DrawString(billText, fontBody, Brushes.Black, new RectangleF(content.Left, y + 22f, leftColW, infoH));

        g.DrawString("SHIP TO", fontSmallBold, Brushes.MidnightBlue, new RectangleF(rightColX, y, leftColW, 14f));
        g.DrawLine(penLight, rightColX, y + 16f, rightColX + leftColW, y + 16f);
        var shipText =
            $"{Safe(customer.Name)}\n" +
            $"{Safe(customer.Phone)}\n" +
            $"{Safe(customer.Email)}";
        g.DrawString(shipText, fontBody, Brushes.Black, new RectangleF(rightColX, y + 22f, leftColW, infoH));

         y += infoH + 22f;

        float tableX = content.Left;
        float tableW = content.Width;
        float tableTop = y;

        float rowHeight = 17f;
        float headerHeight = 18f;

        float descW = tableW * 0.51f;
        float qtyW = tableW * 0.14f;
        float unitW = tableW * 0.23f;
        float amtW = tableW - descW - qtyW - unitW;

        var hdrRect = new RectangleF(tableX, tableTop, tableW, headerHeight);
        g.DrawRectangle(penHeader, hdrRect.X, hdrRect.Y, hdrRect.Width, hdrRect.Height);
        g.FillRectangle(brushHeaderFill, hdrRect.X, hdrRect.Y, hdrRect.Width, hdrRect.Height);
         g.DrawLine(penLight, tableX + descW, tableTop, tableX + descW, tableTop + headerHeight);
        g.DrawLine(penLight, tableX + descW + qtyW, tableTop, tableX + descW + qtyW, tableTop + headerHeight);
        g.DrawLine(penLight, tableX + descW + qtyW + unitW, tableTop, tableX + descW + qtyW + unitW, tableTop + headerHeight);

        g.DrawString(
           "DESCRIPTION",
           fontTableHeader,
           Brushes.White,
           new RectangleF(tableX + 6, tableTop, descW - 12, headerHeight),
            sfCenter);

        g.DrawString(
           "QTY",
           fontTableHeader,
            Brushes.White,
           new RectangleF(tableX + descW + 6, tableTop, qtyW - 12, headerHeight),
           sfCenter);

        g.DrawString(
           "UNIT PRICE",
           fontTableHeader,
           Brushes.White,
           new RectangleF(tableX + descW + qtyW + 6, tableTop, unitW - 12, headerHeight),
           sfCenter);

        g.DrawString(
           "TOTAL",
           fontTableHeader,
           Brushes.White,
           new RectangleF(tableX + descW + qtyW + unitW + 6, tableTop, amtW - 12, headerHeight),
           sfCenter);

        float bodyTop = tableTop + headerHeight;
        float bodyH = content.Bottom - bodyTop - 186f;
        var bodyRect = new RectangleF(tableX, bodyTop, tableW, bodyH);
        g.DrawRectangle(penLight, bodyRect.X, bodyRect.Y, bodyRect.Width, bodyRect.Height);

        float curY = bodyTop;
        int startIndex = state.ItemIndex;

        while (state.ItemIndex < state.Items.Count)
        {
            var (description, qtyText, unitPrice, amount) = state.Items[state.ItemIndex];

            float thisRowH = rowHeight;


            if (curY + thisRowH > bodyRect.Bottom - 4)
                break;

            g.DrawLine(penLight, tableX, curY + thisRowH, tableX + tableW, curY + thisRowH);
            g.DrawLine(penLight, tableX + descW, curY, tableX + descW, curY + thisRowH);
            g.DrawLine(penLight, tableX + descW + qtyW, curY, tableX + descW + qtyW, curY + thisRowH);
            g.DrawLine(penLight, tableX + descW + qtyW + unitW, curY, tableX + descW + qtyW + unitW, curY + thisRowH);
            var descRect = new RectangleF(tableX + 6, curY + 2, descW - 12, thisRowH - 4);
            var qtyRect = new RectangleF(tableX + descW + 6, curY + 2, qtyW - 12, thisRowH - 4);
            var unitRect = new RectangleF(tableX + descW + qtyW + 6, curY + 2, unitW - 12, thisRowH - 4);
            var amtRect = new RectangleF(tableX + descW + qtyW + unitW + 6, curY + 2, amtW - 12, thisRowH - 4);


            g.DrawString(description, fontTable, Brushes.Black, descRect, sfNearTop);
            g.DrawString(qtyText, fontTable, Brushes.Black, qtyRect, sfFarTop);
            g.DrawString(unitPrice.ToString("0.00", CultureInfo.CurrentCulture), fontTable, Brushes.Black, unitRect, sfFarTop);
            g.DrawString(amount.ToString("0.00", CultureInfo.CurrentCulture), fontTable, Brushes.Black, amtRect, sfFarTop);

            curY += thisRowH;
            state.ItemIndex++;
        }

         float summaryTop = bodyRect.Bottom + 6f;
        float summaryX = content.Right - (tableW * 0.35f);
        float summaryW = tableW * 0.35f;
        float summaryRowH = 15f;

         var summaryRows = new List<(string Label, string Value)>
        {
            ("SUBTOTAL", subtotal.ToString("0.00", CultureInfo.CurrentCulture)),
            ("DISCOUNT", discount.ToString("0.00", CultureInfo.CurrentCulture)),
            ("SUBTOTAL LESS DISCOUNT", Math.Max(0m, subtotal - discount).ToString("0.00", CultureInfo.CurrentCulture)),
            ("TOTAL TAX (VAT)", vat.ToString("0.00", CultureInfo.CurrentCulture)),
            ("PAYMENT", paymentMethod)
        };
       if (paymentMethod.Equals("CASH", StringComparison.OrdinalIgnoreCase))
        {
            summaryRows.Add(("CASH", totalTendered.ToString("0.00", CultureInfo.CurrentCulture)));
            summaryRows.Add(("CHANGE", change.ToString("0.00", CultureInfo.CurrentCulture)));
        }

       for (int i = 0; i < summaryRows.Count; i++)
        {
            var rowY = summaryTop + (i * summaryRowH);
            g.DrawString(summaryRows[i].Label, fontSmallBold, Brushes.MidnightBlue, new RectangleF(summaryX, rowY, summaryW * 0.6f, summaryRowH), sfFarCenter);

        g.DrawString(summaryRows[i].Value, fontSmall, Brushes.Black, new RectangleF(summaryX + (summaryW * 0.62f), rowY, summaryW * 0.38f, summaryRowH), sfFarCenter);
        }

           var remarksText = Safe(remarks);
        if (!string.IsNullOrWhiteSpace(remarksText))
        {
            g.DrawString(remarksText, fontRemarks, Brushes.Black, new RectangleF(content.Left + 2f, summaryTop + 44f, content.Width * 0.6f, 90f), sfCenter);
        }

         float paidY = summaryTop + (summaryRows.Count * summaryRowH) + 8f;
        g.DrawLine(penDark, summaryX, paidY, summaryX + summaryW, paidY);
        g.DrawString("Paid", fontPaid, Brushes.Black, new RectangleF(summaryX, paidY + 4f, summaryW * 0.2f, 20f), sfFarCenter);
        g.DrawString("$", fontPaid, Brushes.Black, new RectangleF(summaryX + summaryW * 0.24f, paidY + 4f, summaryW * 0.09f, 20f), sfCenter);
         var paidAmount = Math.Round(Math.Max(0m, totalDue), 2, MidpointRounding.AwayFromZero);
        var paidAmountText = paidAmount.ToString("0.00", CultureInfo.CurrentCulture);
        g.DrawString(paidAmountText, fontPaid, Brushes.Black, new RectangleF(summaryX + summaryW * 0.7f, paidY + 4f, summaryW * 0.24f, 20f), sfFarCenter);
        g.DrawLine(penDark, summaryX, paidY + 28f, summaryX + summaryW, paidY + 28f);

        g.FillRectangle(brushHeaderFill, new RectangleF(content.Left, content.Bottom - stripeH - 4f, content.Width, stripeH));

        bool hasMore = state.ItemIndex < state.Items.Count;

        if (hasMore && state.ItemIndex == startIndex)
            state.ItemIndex = Math.Min(state.Items.Count, startIndex + 1);

        return hasMore;
    }
    
    private static bool TryLoadLogoImage(string logoPath, out Image? logoImage)
    {
        logoImage = null;

        if (string.IsNullOrWhiteSpace(logoPath) || !File.Exists(logoPath))
            return false;

        try
        {
            logoImage = Image.FromFile(logoPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

}
