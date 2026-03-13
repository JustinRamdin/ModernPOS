using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

using Pos.Terminal.Models;

namespace Pos.Terminal.Services;

[SupportedOSPlatform("windows")]
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

        // Keep the receipt content proportional to A4/letter printables so printers with
        // larger hard-margins still fit all sections on a single page.
        const float baselineWidth = 807f;
        const float baselineHeight = 1149f;
        var pageScale = Math.Min(content.Width / baselineWidth, content.Height / baselineHeight);
        var scale = Math.Clamp(pageScale, 0.72f, 1f);
        float S(float value) => value * scale;

        using var fontBody = new Font("Segoe UI", S(9f), FontStyle.Regular);
        using var fontSmall = new Font("Segoe UI", S(8f), FontStyle.Regular);
        using var fontSmallBold = new Font("Segoe UI", S(8f), FontStyle.Bold);
        using var fontTitle = new Font("Segoe UI", S(21f), FontStyle.Regular);
        using var fontLogo = new Font("Segoe UI", S(15f), FontStyle.Regular);
        using var fontTableHeader = new Font("Segoe UI", S(8.5f), FontStyle.Bold);
        using var fontTable = new Font("Segoe UI", S(8.5f), FontStyle.Regular);
        using var fontPaid = new Font("Segoe UI", S(12f), FontStyle.Bold);
        using var fontRemarks = new Font("Segoe UI", S(9.5f), FontStyle.Regular);

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

        float y = content.Top + S(4f);
        float stripeH = S(12f);
        g.FillRectangle(brushHeaderFill, new RectangleF(content.Left, y, content.Width, stripeH));
        y += stripeH + 30f;

        g.DrawString("RECEIPT", fontTitle, Brushes.DimGray, new RectangleF(content.Left + S(2f), y, content.Width * 0.45f, S(40f)));

        // --- LOGO + META (logo larger, meta moved below logo) ---
        float metaW = S(95f);
        float rightPad = S(16f);    // safe on printers with larger hard-margins
        float topPad = S(6f);
        float gapBelowLogo = S(10f);

        float metaX = content.Right - metaW - rightPad;

        static RectangleF FitToBoxPreserveAspect(SizeF img, RectangleF box)
        {
            if (img.Width <= 0 || img.Height <= 0) return box;

            float scale = Math.Min(box.Width / img.Width, box.Height / img.Height);
            float w = img.Width * scale;
            float h = img.Height * scale;

            float x = box.X + (box.Width - w) / 2f;
            float y = box.Y + (box.Height - h) / 2f;

            return new RectangleF(x, y, w, h);
        }

        var logoMultiplier = Math.Clamp(settings.LogoScaleMultiplier, 1, 4);

        // Bigger logo box (especially for wide logos like SGLTT)
         float desiredLogoW = S(240f) * logoMultiplier;
        float desiredLogoH = S(120f) * logoMultiplier;

        // Place logo under the red stripe, right-aligned
        float logoTop = content.Top + stripeH + topPad;
        float boxW = Math.Min(desiredLogoW, content.Width * 0.50f);
        float boxH = Math.Min(desiredLogoH, 140f * logoMultiplier);

        var logoBox = new RectangleF(
            x: content.Right - boxW - rightPad,
            y: logoTop,
            width: boxW,
            height: boxH
        );

        if (TryLoadLogoImage(settings.LogoImagePath, out var logoImage) && logoImage is not null)
        {
            using (logoImage)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                var drawRect = FitToBoxPreserveAspect(logoImage.PhysicalDimension, logoBox);
                g.DrawImage(logoImage, drawRect);
            }
        }
        else
        {
            g.FillEllipse(brushLogo, logoBox);
            g.DrawString("LOGO", fontLogo, Brushes.White, logoBox, sfCenter);
        }

        // Meta goes BELOW the logo now
        float metaY = logoBox.Bottom + gapBelowLogo;

        g.DrawString("PAYMENT DATE", fontSmallBold, Brushes.MidnightBlue,
            new RectangleF(metaX, metaY, metaW, S(16f)), sfFarCenter);
        g.DrawLine(penLight, metaX, metaY + S(18f), metaX + metaW, metaY + S(18f));
        g.DrawString(invoiceDate.ToString("yyyy-MM-dd"), fontSmall, Brushes.Black,
           new RectangleF(metaX, metaY + S(20f), metaW, S(14f)), sfFarCenter);

        g.DrawString("RECEIPT NO.", fontSmallBold, Brushes.MidnightBlue,
            new RectangleF(metaX, metaY + S(40f), metaW, S(16f)), sfFarCenter);
        g.DrawLine(penLight, metaX, metaY + S(58f), metaX + metaW, metaY + S(58f));
        g.DrawString(receiptNo, fontSmall, Brushes.Black,
            new RectangleF(metaX, metaY + S(60f), metaW, S(14f)), sfFarCenter);

        var companyText =
            $"{Safe(settings.CompanyName)}\n" +
            $"{Safe(settings.CompanyAddress)}\n" +
            $"{Safe(settings.CompanyContact)}";
         g.DrawString(companyText, fontBody, Brushes.Black, new RectangleF(content.Left + S(2f), y + S(55f), content.Width * 0.5f, S(84f)));

        y += S(165f);

        float leftColW = content.Width * 0.44f;
        float gap = S(26f);
        float rightColX = content.Left + leftColW + gap;
        float infoH = S(110f);

        g.DrawString("BILL TO", fontSmallBold, Brushes.MidnightBlue, new RectangleF(content.Left, y, leftColW, S(14f)));
        g.DrawLine(penLight, content.Left, y + S(16f), content.Left + leftColW, y + S(16f));
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
        g.DrawString(shipText, fontBody, Brushes.Black, new RectangleF(rightColX, y + S(22f), leftColW, infoH));

         y += infoH + S(22f);

        float tableX = content.Left;
        float tableW = content.Width;
        float tableTop = y;

        float rowHeight = S(17f);
        float headerHeight = S(18f);

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
            new RectangleF(tableX + S(6f), tableTop, descW - S(12f), headerHeight),
            sfCenter);

        g.DrawString(
            "QTY",
            fontTableHeader,
            Brushes.White,
             new RectangleF(tableX + descW + S(6f), tableTop, qtyW - S(12f), headerHeight),
            sfCenter);

        g.DrawString(
            "UNIT PRICE",
            fontTableHeader,
            Brushes.White,
            new RectangleF(tableX + descW + qtyW + S(6f), tableTop, unitW - S(12f), headerHeight),
            sfCenter);

        g.DrawString(
            "TOTAL",
            fontTableHeader,
            Brushes.White,
            new RectangleF(tableX + descW + qtyW + unitW + S(6f), tableTop, amtW - S(12f), headerHeight),
            sfCenter);

        float bodyTop = tableTop + headerHeight;
        float bodyH = content.Bottom - bodyTop - S(186f);
        var bodyRect = new RectangleF(tableX, bodyTop, tableW, bodyH);
        g.DrawRectangle(penLight, bodyRect.X, bodyRect.Y, bodyRect.Width, bodyRect.Height);

        float curY = bodyTop;
        int startIndex = state.ItemIndex;

        while (state.ItemIndex < state.Items.Count)
        {
            var (description, qtyText, unitPrice, amount) = state.Items[state.ItemIndex];

            float thisRowH = rowHeight;

            if (curY + thisRowH > bodyRect.Bottom - S(4f))
                break;

            g.DrawLine(penLight, tableX, curY + thisRowH, tableX + tableW, curY + thisRowH);
            g.DrawLine(penLight, tableX + descW, curY, tableX + descW, curY + thisRowH);
            g.DrawLine(penLight, tableX + descW + qtyW, curY, tableX + descW + qtyW, curY + thisRowH);
            g.DrawLine(penLight, tableX + descW + qtyW + unitW, curY, tableX + descW + qtyW + unitW, curY + thisRowH);

            var descRect = new RectangleF(tableX + S(6f), curY + S(2f), descW - S(12f), thisRowH - S(4f));
            var qtyRect = new RectangleF(tableX + descW + S(6f), curY + S(2f), qtyW - S(12f), thisRowH - S(4f));
            var unitRect = new RectangleF(tableX + descW + qtyW + S(6f), curY + S(2f), unitW - S(12f), thisRowH - S(4f));
            var amtRect = new RectangleF(tableX + descW + qtyW + unitW + S(6f), curY + S(2f), amtW - S(12f), thisRowH - S(4f));

            g.DrawString(description, fontTable, Brushes.Black, descRect, sfNearTop);
            g.DrawString(qtyText, fontTable, Brushes.Black, qtyRect, sfFarTop);
            g.DrawString(unitPrice.ToString("0.00", CultureInfo.CurrentCulture), fontTable, Brushes.Black, unitRect, sfFarTop);
            g.DrawString(amount.ToString("0.00", CultureInfo.CurrentCulture), fontTable, Brushes.Black, amtRect, sfFarTop);

            curY += thisRowH;
            state.ItemIndex++;
        }

        float summaryTop = bodyRect.Bottom + S(6f);
        float summaryX = content.Right - (tableW * 0.35f);
        float summaryW = tableW * 0.35f;
        float summaryRowH = S(15f);

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
           g.DrawString(remarksText, fontRemarks, Brushes.Black, new RectangleF(content.Left + S(2f), summaryTop + S(44f), content.Width * 0.6f, S(90f)), sfCenter);
        }

        float paidY = summaryTop + (summaryRows.Count * summaryRowH) + S(8f);
        g.DrawLine(penDark, summaryX, paidY, summaryX + summaryW, paidY);
        g.DrawString("Paid", fontPaid, Brushes.Black, new RectangleF(summaryX, paidY + S(4f), summaryW * 0.2f, S(20f)), sfFarCenter);
        g.DrawString("$", fontPaid, Brushes.Black, new RectangleF(summaryX + summaryW * 0.24f, paidY + S(4f), summaryW * 0.09f, S(20f)), sfCenter);
        var paidAmount = Math.Round(Math.Max(0m, totalDue), 2, MidpointRounding.AwayFromZero);
        var paidAmountText = paidAmount.ToString("0.00", CultureInfo.CurrentCulture);
        g.DrawString(paidAmountText, fontPaid, Brushes.Black, new RectangleF(summaryX + summaryW * 0.7f, paidY + S(4f), summaryW * 0.24f, S(20f)), sfFarCenter);
        g.DrawLine(penDark, summaryX, paidY + S(28f), summaryX + summaryW, paidY + S(28f));

        g.FillRectangle(brushHeaderFill, new RectangleF(content.Left, content.Bottom - stripeH - S(4f), content.Width, stripeH));

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
            // Load without locking the file (lets user replace logo while app is running)
            using var fs = new FileStream(logoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            ms.Position = 0;

            logoImage = Image.FromStream(ms);
            return true;
        }
        catch
        {
            return false;
        }
    }
}