using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MapoMyslacz.Core.Export;
using MapoMyslacz.Core.Model;
using static MapoMyslacz.Services.PrintNative;

namespace MapoMyslacz.Services;

/// <summary>
/// Drukowanie przez okno „Drukuj” Windows i GDI (działa z Native AOT, bez WinRT-owego PrintManagera).
/// Każda strona jest renderowana przez Win2D w rozdzielczości drukarki i wysyłana pasami (StretchDIBits).
/// </summary>
internal static class PrintService
{
    public sealed record Result(bool Printed, string? Error, int Pages = 0);

    /// <summary>Renderuje stronę planu (bitmapa BGRA całej kartki w zadanym DPI).</summary>
    public delegate (byte[] Bgra, int Width, int Height) PageRenderer(PageTile page, PrintPlan plan, int number, int count, double dpi);

    /// <summary>
    /// Pokazuje okno drukowania, ustawia orientację (z opcji albo dopasowaną do mapy), dzieli mapę na strony i drukuje.
    /// </summary>
    public static Result Print(nint hwnd, Box content, PrintOptions options, string title, PageRenderer render)
    {
        var pd = new PRINTDLGW
        {
            lStructSize = (uint)Unsafe.SizeOf<PRINTDLGW>(),
            hwndOwner = hwnd,
            Flags = PD_RETURNDC | PD_NOPAGENUMS | PD_NOSELECTION | PD_USEDEVMODECOPIESANDCOLLATE | PD_HIDEPRINTTOFILE,
            nCopies = 1,
            nFromPage = 1, nToPage = 1, nMinPage = 1, nMaxPage = 1,
        };
        if (!PrintDlgW(ref pd))
        {
            uint err = CommDlgExtendedError();
            return new Result(false, err != 0 ? $"Nie udało się otworzyć okna drukowania (kod 0x{err:X})." : null);
        }
        try
        {
            if (pd.hDC == 0) return new Result(false, "Drukarka nie zwróciła kontekstu urządzenia.");

            // orientacja: wybrana w ustawieniach albo dopasowana do kształtu mapy
            bool landscape = options.Orientation switch
            {
                PageOrientation.Landscape => true,
                PageOrientation.Portrait => false,
                _ => content.Width > content.Height,
            };
            SetOrientation(pd.hDC, pd.hDevMode, landscape);

            int dpiX = GetDeviceCaps(pd.hDC, LOGPIXELSX), dpiY = GetDeviceCaps(pd.hDC, LOGPIXELSY);
            if (dpiX <= 0 || dpiY <= 0) return new Result(false, "Nieprawidłowa rozdzielczość drukarki.");
            int physW = GetDeviceCaps(pd.hDC, PHYSICALWIDTH), physH = GetDeviceCaps(pd.hDC, PHYSICALHEIGHT);
            int offX = GetDeviceCaps(pd.hDC, PHYSICALOFFSETX), offY = GetDeviceCaps(pd.hDC, PHYSICALOFFSETY);
            if (physW <= 0 || physH <= 0)
            {
                physW = GetDeviceCaps(pd.hDC, HORZRES);
                physH = GetDeviceCaps(pd.hDC, VERTRES);
                offX = offY = 0;
            }
            double pageW = physW * 25.4 / dpiX, pageH = physH * 25.4 / dpiY;

            // strony liczymy dla kartki w orientacji, którą faktycznie ma drukarka
            var o = options.Clone();
            o.Orientation = pageW > pageH ? PageOrientation.Landscape : PageOrientation.Portrait;
            var plan = PrintLayout.Plan(content, Math.Min(pageW, pageH), Math.Max(pageW, pageH), o);
            double dpi = Math.Clamp(Math.Min(Math.Max(dpiX, dpiY), options.Dpi), 96, 600);

            var docName = Marshal.StringToHGlobalUni($"MapoMyślacz – {title}");
            try
            {
                var di = new DOCINFOW { cbSize = Unsafe.SizeOf<DOCINFOW>(), lpszDocName = docName };
                if (StartDocW(pd.hDC, ref di) <= 0) return new Result(false, "Drukarka odrzuciła zadanie drukowania.");
                bool done = false;
                try
                {
                    int n = 0;
                    foreach (var page in plan.Pages)
                    {
                        var (px, bw, bh) = render(page, plan, ++n, plan.Pages.Count, dpi);
                        if (StartPage(pd.hDC) <= 0) throw new InvalidOperationException("Błąd podczas rozpoczynania strony.");
                        SetStretchBltMode(pd.hDC, HALFTONE);
                        SetBrushOrgEx(pd.hDC, 0, 0, 0);
                        // obraz całej kartki; obszar druku zaczyna się PHYSICALOFFSET od jej krawędzi
                        BlitBanded(pd.hDC, px, bw, bh, -offX, -offY, physW, physH);
                        if (EndPage(pd.hDC) <= 0) throw new InvalidOperationException("Błąd podczas kończenia strony.");
                    }
                    EndDoc(pd.hDC);
                    done = true;
                    return new Result(true, null, plan.Pages.Count);
                }
                finally
                {
                    if (!done) AbortDoc(pd.hDC);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(docName);
            }
        }
        catch (Exception ex)
        {
            return new Result(false, ex.Message);
        }
        finally
        {
            if (pd.hDC != 0) DeleteDC(pd.hDC);
            if (pd.hDevMode != 0) GlobalFree(pd.hDevMode);
            if (pd.hDevNames != 0) GlobalFree(pd.hDevNames);
        }
    }

    /// <summary>Ustawia orientację w DEVMODE drukarki i odświeża kontekst urządzenia.</summary>
    private static unsafe void SetOrientation(nint hdc, nint devMode, bool landscape)
    {
        if (devMode == 0) return;
        nint dm = GlobalLock(devMode);
        if (dm == 0) return;
        try
        {
            *(uint*)((byte*)dm + DevModeFieldsOffset) |= DM_ORIENTATION;
            *(short*)((byte*)dm + DevModeOrientationOffset) = landscape ? DMORIENT_LANDSCAPE : DMORIENT_PORTRAIT;
            ResetDCW(hdc, dm);
        }
        finally
        {
            GlobalUnlock(devMode);
        }
    }

    /// <summary>Wysyła bitmapę pasami po 256 wierszy (sterowniki drukarek źle znoszą jeden ogromny obraz).</summary>
    private static unsafe void BlitBanded(nint hdc, byte[] bgra, int bw, int bh, int dx, int dy, int dw, int dh)
    {
        const int band = 256;
        fixed (byte* p = bgra)
        {
            for (int row = 0; row < bh; row += band)
            {
                int rows = Math.Min(band, bh - row);
                var bmi = new BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(BITMAPINFOHEADER),
                    biWidth = bw,
                    biHeight = -rows, // od góry do dołu
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0, // BI_RGB
                };
                int y0 = dy + (int)Math.Round((double)row * dh / bh);
                int y1 = dy + (int)Math.Round((double)(row + rows) * dh / bh);
                StretchDIBits(hdc, dx, y0, dw, y1 - y0, 0, 0, bw, rows, p + (long)row * bw * 4, &bmi, DIB_RGB_COLORS, SRCCOPY);
            }
        }
    }
}

/// <summary>Minimalny zestaw Win32 do druku — przez LibraryImport, więc działa z Native AOT.</summary>
internal static unsafe partial class PrintNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PRINTDLGW
    {
        public uint lStructSize;
        public nint hwndOwner;
        public nint hDevMode;
        public nint hDevNames;
        public nint hDC;
        public uint Flags;
        public ushort nFromPage;
        public ushort nToPage;
        public ushort nMinPage;
        public ushort nMaxPage;
        public ushort nCopies;
        public nint hInstance;
        public nint lCustData;
        public nint lpfnPrintHook;
        public nint lpfnSetupHook;
        public nint lpPrintTemplateName;
        public nint lpSetupTemplateName;
        public nint hPrintTemplate;
        public nint hSetupTemplate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DOCINFOW
    {
        public int cbSize;
        public nint lpszDocName;
        public nint lpszOutput;
        public nint lpszDatatype;
        public uint fwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    public const uint PD_NOSELECTION = 0x4, PD_NOPAGENUMS = 0x8, PD_RETURNDC = 0x100;
    public const uint PD_USEDEVMODECOPIESANDCOLLATE = 0x40000, PD_HIDEPRINTTOFILE = 0x100000;
    public const int HORZRES = 8, VERTRES = 10, LOGPIXELSX = 88, LOGPIXELSY = 90;
    public const int PHYSICALWIDTH = 110, PHYSICALHEIGHT = 111, PHYSICALOFFSETX = 112, PHYSICALOFFSETY = 113;
    public const int HALFTONE = 4;
    public const uint DIB_RGB_COLORS = 0, SRCCOPY = 0x00CC0020;

    // DEVMODEW: dmFields po 32 znakach nazwy (64 B) + 4 × WORD = przesunięcie 72; dmOrientation zaraz za nim (76)
    public const int DevModeFieldsOffset = 72, DevModeOrientationOffset = 76;
    public const uint DM_ORIENTATION = 0x1;
    public const short DMORIENT_PORTRAIT = 1, DMORIENT_LANDSCAPE = 2;

    [LibraryImport("comdlg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PrintDlgW(ref PRINTDLGW lppd);

    [LibraryImport("comdlg32.dll")]
    internal static partial uint CommDlgExtendedError();

    [LibraryImport("gdi32.dll")]
    internal static partial int StartDocW(nint hdc, ref DOCINFOW lpdi);

    [LibraryImport("gdi32.dll")]
    internal static partial int StartPage(nint hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial int EndPage(nint hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial int EndDoc(nint hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial int AbortDoc(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial nint ResetDCW(nint hdc, nint devmode);

    [LibraryImport("gdi32.dll")]
    internal static partial int GetDeviceCaps(nint hdc, int index);

    [LibraryImport("gdi32.dll")]
    internal static partial int SetStretchBltMode(nint hdc, int mode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetBrushOrgEx(nint hdc, int x, int y, nint lppt);

    [LibraryImport("gdi32.dll")]
    internal static partial int StretchDIBits(nint hdc, int xDest, int yDest, int destWidth, int destHeight,
        int xSrc, int ySrc, int srcWidth, int srcHeight, void* lpBits, BITMAPINFOHEADER* lpbmi, uint iUsage, uint rop);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalFree(nint hMem);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalUnlock(nint hMem);
}
