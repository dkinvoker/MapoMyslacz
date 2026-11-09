using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace MapoMyslacz.Core.Export;

/// <summary>Strona PDF: obraz całej kartki (BGRA, 8 bitów na kanał, wiersze od góry).</summary>
public sealed record PdfPage(double WidthMm, double HeightMm, int PixelWidth, int PixelHeight, byte[] Bgra);

/// <summary>
/// Minimalny zapis PDF 1.4: każda strona to jeden obraz (bezstratny, kompresja Flate) na całą kartkę.
/// Bez zewnętrznych bibliotek — wystarcza do druku i wysyłania map.
/// </summary>
public static class PdfWriter
{
    public static void Write(Stream output, IReadOnlyList<PdfPage> pages, string title, string producer)
    {
        if (pages.Count == 0) throw new ArgumentException("PDF musi mieć co najmniej jedną stronę.", nameof(pages));
        const double pt = 72 / 25.4;
        var inv = CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.###", inv);

        // obiekty: 1 katalog, 2 drzewo stron, potem dla każdej strony: obraz, strona, treść; na końcu informacje
        int ObjImage(int i) => 3 + i * 3;
        int ObjPage(int i) => 4 + i * 3;
        int ObjContent(int i) => 5 + i * 3;
        int infoObj = 3 + pages.Count * 3;

        var offsets = new long[infoObj + 1];
        long start = output.Position;
        void W(string s) => output.Write(Encoding.Latin1.GetBytes(s));
        void Begin(int num)
        {
            offsets[num] = output.Position - start;
            W($"{num} 0 obj\n");
        }
        void Obj(int num, string body)
        {
            Begin(num);
            W($"{body}\nendobj\n");
        }
        void Stream(int num, string dict, byte[] data)
        {
            Begin(num);
            W($"<< {dict} /Length {data.Length} >>\nstream\n");
            output.Write(data);
            W("\nendstream\nendobj\n");
        }

        W("%PDF-1.4\n%âãÏÓ\n");
        Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Obj(2, $"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{ObjPage(i)} 0 R"))}] /Count {pages.Count} >>");

        for (int i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            if (p.Bgra.Length < p.PixelWidth * p.PixelHeight * 4) throw new ArgumentException($"Strona {i + 1}: za mało danych obrazu.");
            Stream(ObjImage(i), $"/Type /XObject /Subtype /Image /Width {p.PixelWidth} /Height {p.PixelHeight} " +
                                "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode", DeflateRgb(p.Bgra, p.PixelWidth * p.PixelHeight));
            double w = p.WidthMm * pt, h = p.HeightMm * pt;
            Obj(ObjPage(i), $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(w)} {F(h)}] " +
                            $"/Resources << /XObject << /Im0 {ObjImage(i)} 0 R >> >> /Contents {ObjContent(i)} 0 R >>");
            Stream(ObjContent(i), "", Encoding.ASCII.GetBytes($"q {F(w)} 0 0 {F(h)} 0 0 cm /Im0 Do Q\n"));
        }

        Obj(infoObj, $"<< /Title {Text(title)} /Producer {Text(producer)} /CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >>");

        long xref = output.Position - start;
        var sb = new StringBuilder();
        sb.Append($"xref\n0 {infoObj + 1}\n0000000000 65535 f \n");
        for (int n = 1; n <= infoObj; n++) sb.Append($"{offsets[n]:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {infoObj + 1} /Root 1 0 R /Info {infoObj} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        W(sb.ToString());
    }

    private static byte[] DeflateRgb(byte[] bgra, int pixels)
    {
        var rgb = new byte[pixels * 3];
        for (int i = 0, j = 0; j < rgb.Length; i += 4, j += 3)
        {
            rgb[j] = bgra[i + 2];
            rgb[j + 1] = bgra[i + 1];
            rgb[j + 2] = bgra[i];
        }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(rgb);
        return ms.ToArray();
    }

    /// <summary>Tekst PDF jako UTF-16BE (polskie znaki w tytule).</summary>
    private static string Text(string s)
    {
        var sb = new StringBuilder("<FEFF");
        foreach (var c in s) sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
        return sb.Append('>').ToString();
    }
}
