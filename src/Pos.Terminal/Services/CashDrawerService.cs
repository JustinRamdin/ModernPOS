using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pos.Terminal.Services;

internal static class CashDrawerService
{
    public static bool TryOpen(string printerName, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(printerName))
        {
            error = "No receipt printer selected.";
            return false;
        }

        IntPtr printerHandle = IntPtr.Zero;
        IntPtr documentHandle = IntPtr.Zero;
        IntPtr pageHandle = IntPtr.Zero;
        IntPtr dataHandle = IntPtr.Zero;

        try
        {
            if (!OpenPrinter(printerName, out printerHandle, IntPtr.Zero))
            {
                error = $"Unable to access printer '{printerName}'.";
                return false;
            }

            var documentInfo = new DOCINFOA
            {
                pDocName = "ModernPOS Cash Drawer Kick",
                pDataType = "RAW"
            };

            documentHandle = StartDocPrinter(printerHandle, 1, documentInfo);
            if (documentHandle == IntPtr.Zero)
            {
                error = "Failed to start raw printer document.";
                return false;
            }

            if (!StartPagePrinter(printerHandle))
            {
                error = "Failed to start raw printer page.";
                return false;
            }

            pageHandle = new IntPtr(1);
            // Most receipt printers wire drawers to pin 2 (m=0) or pin 5 (m=1).
            // Send both pulse variants so either wiring opens the drawer.
            var commands = new[]
            {
                new byte[] { 0x07 },
                new byte[] { 0x1B, 0x70, 0x00, 0x19, 0xFA }, // ESC/POS ESC p m=0 t1 t2 (pin 2)
                new byte[] { 0x1B, 0x70, 0x01, 0x19, 0xFA }, // ESC/POS ESC p m=1 t1 t2 (pin 5)
                new byte[] { 0x1B, 0x07, 0x00 },             // Star ESC BEL n=0
                new byte[] { 0x1B, 0x07, 0x01 },             // Star ESC BEL n=1
                new byte[] { 0x10, 0x14, 0x01, 0x00, 0x05 }, // Star DLE DC4 peripheral 1
                new byte[] { 0x10, 0x14, 0x01, 0x01, 0x05 }  // Star DLE DC4 peripheral 2
            };

            Exception? lastWriteFailure = null;
            var wroteAnyCommand = false;

            foreach (var command in commands)
            {
                if (dataHandle != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(dataHandle);
                    dataHandle = IntPtr.Zero;
                }

                dataHandle = Marshal.AllocCoTaskMem(command.Length);
                Marshal.Copy(command, 0, dataHandle, command.Length);

                try
                {
                    if (WritePrinter(printerHandle, dataHandle, command.Length, out var written) && written == command.Length)
                        wroteAnyCommand = true;
                }
                catch (Exception ex)
                {
                    lastWriteFailure = ex;
                }
            }

            if (!wroteAnyCommand)
            {
                var win32 = Marshal.GetLastWin32Error();
                var win32Message = win32 != 0
                    ? $" (Win32 {win32}: {new Win32Exception(win32).Message})"
                    : string.Empty;

                error = lastWriteFailure is not null
                    ? $"Failed to write cash drawer command to printer: {lastWriteFailure.Message}{win32Message}"
                    : $"Failed to write cash drawer command to printer.{win32Message}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Cash drawer signal failed: {ex.Message}";
            return false;
        }
        finally
        {
            if (pageHandle != IntPtr.Zero)
                EndPagePrinter(printerHandle);

            if (documentHandle != IntPtr.Zero)
                EndDocPrinter(printerHandle);

            if (printerHandle != IntPtr.Zero)
                ClosePrinter(printerHandle);

            if (dataHandle != IntPtr.Zero)
                Marshal.FreeCoTaskMem(dataHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string pDocName;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)]
        public string pDataType;
    }

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFOA di);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);
}
