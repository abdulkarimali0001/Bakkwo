using System.Globalization;
using System.Text;

namespace Bakkwo.Core.Pdf;

/// <summary>One page: a baseline RGB JPEG and its pixel size.</summary>
public sealed record JpegPage(byte[] Jpeg, int PixelWidth, int PixelHeight);

/// <summary>
/// A tiny PDF 1.4 writer that places one JPEG per A4 page. JPEG data is embedded as-is
/// (DCTDecode), so there is no re-compression and no PDF library dependency.
/// </summary>
public static class PdfImageWriter
{
    private const double A4Width = 595.28;  // points (1/72 inch)
    private const double A4Height = 841.89;
    private const double Margin = 28;

    public static byte[] Build(IReadOnlyList<JpegPage> pages)
    {
        if (pages.Count == 0) throw new ArgumentException("A PDF needs at least one page.", nameof(pages));

        using var pdf = new MemoryStream();
        var offsets = new List<long> { 0 }; // object 0 is the free-list head

        void Raw(string text) => pdf.Write(Encoding.ASCII.GetBytes(text));
        void BeginObject(int number)
        {
            while (offsets.Count <= number) offsets.Add(0);
            offsets[number] = pdf.Position;
            Raw($"{number} 0 obj\n");
        }

        // Header; the second line marks the file as binary for transfer tools.
        Raw("%PDF-1.4\n");
        pdf.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

        // Objects: 1 catalog, 2 page tree, then per page i: page (3+3i), image (4+3i), content (5+3i).
        BeginObject(1);
        Raw("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{3 + 3 * i} 0 R"));
        BeginObject(2);
        Raw($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>\nendobj\n");

        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var landscape = page.PixelWidth > page.PixelHeight;
            var pageWidth = landscape ? A4Height : A4Width;
            var pageHeight = landscape ? A4Width : A4Height;

            // Fit inside the margins; never blow small images up past 1 px = 1 pt.
            var scale = Math.Min(1.0, Math.Min(
                (pageWidth - 2 * Margin) / page.PixelWidth,
                (pageHeight - 2 * Margin) / page.PixelHeight));
            var drawWidth = page.PixelWidth * scale;
            var drawHeight = page.PixelHeight * scale;
            var x = (pageWidth - drawWidth) / 2;
            var y = (pageHeight - drawHeight) / 2;

            int pageObj = 3 + 3 * i, imageObj = 4 + 3 * i, contentObj = 5 + 3 * i;

            BeginObject(pageObj);
            Raw($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(pageWidth)} {F(pageHeight)}] " +
                $"/Resources << /XObject << /Im0 {imageObj} 0 R >> /ProcSet [/PDF /ImageC] >> " +
                $"/Contents {contentObj} 0 R >>\nendobj\n");

            BeginObject(imageObj);
            Raw($"<< /Type /XObject /Subtype /Image /Width {page.PixelWidth} /Height {page.PixelHeight} " +
                $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {page.Jpeg.Length} >>\nstream\n");
            pdf.Write(page.Jpeg);
            Raw("\nendstream\nendobj\n");

            var content = $"q {F(drawWidth)} 0 0 {F(drawHeight)} {F(x)} {F(y)} cm /Im0 Do Q";
            BeginObject(contentObj);
            Raw($"<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
        }

        // Cross-reference table: every entry is exactly 20 bytes.
        var xrefOffset = pdf.Position;
        var xref = new StringBuilder();
        xref.Append($"xref\n0 {offsets.Count}\n");
        xref.Append("0000000000 65535 f \n");
        for (var n = 1; n < offsets.Count; n++)
        {
            xref.Append(offsets[n].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }
        xref.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        Raw(xref.ToString());

        return pdf.ToArray();
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
