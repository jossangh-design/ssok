using System.Globalization;
using System.Text;

namespace Ssok.Core.Documents;

/// <summary>PDF 한 쪽을 그림으로 바꾼 것. 크기는 PDF 단위(pt, 1/72인치).</summary>
public sealed record PageImage(byte[] Jpeg, int PixelWidth, int PixelHeight, double WidthPt, double HeightPt, bool Gray);

/// <summary>
/// 쪽 그림(JPEG)들로 새 PDF를 만든다. 쪽마다 그림 하나를 쪽 크기에 꽉 채워 그린다.
/// 외부 부품 없이 PDF 1.4 형식을 직접 쓴다 (JPEG은 DCTDecode 그대로 넣으므로 다시 압축하지 않는다).
/// </summary>
public static class PdfImageWriter
{
    public static void Write(Stream output, IReadOnlyList<PageImage> pages)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0) throw new ArgumentException("쪽이 없습니다.", nameof(pages));

        // 객체 번호: 1 카탈로그, 2 쪽 목록, 그다음 쪽마다 (쪽, 내용, 그림) 3개
        int count = 2 + pages.Count * 3;
        var offsets = new long[count + 1];
        var w = new CountingWriter(output);

        w.Ascii("%PDF-1.4\n%âãÏÓ\n");

        offsets[1] = w.Position;
        w.Ascii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = w.Position;
        var kids = new StringBuilder();
        for (int i = 0; i < pages.Count; i++) kids.Append(3 + i * 3).Append(" 0 R ");
        w.Ascii($"2 0 obj\n<< /Type /Pages /Count {pages.Count} /Kids [ {kids}] >>\nendobj\n");

        for (int i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            int pageObj = 3 + i * 3, contentObj = pageObj + 1, imageObj = pageObj + 2;
            string pw = Num(p.WidthPt), ph = Num(p.HeightPt);

            offsets[pageObj] = w.Position;
            w.Ascii($"{pageObj} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pw} {ph}] " +
                    $"/Resources << /XObject << /Im0 {imageObj} 0 R >> >> /Contents {contentObj} 0 R >>\nendobj\n");

            var content = Encoding.ASCII.GetBytes($"q {pw} 0 0 {ph} 0 0 cm /Im0 Do Q");
            offsets[contentObj] = w.Position;
            w.Ascii($"{contentObj} 0 obj\n<< /Length {content.Length} >>\nstream\n");
            w.Bytes(content);
            w.Ascii("\nendstream\nendobj\n");

            offsets[imageObj] = w.Position;
            w.Ascii($"{imageObj} 0 obj\n<< /Type /XObject /Subtype /Image /Width {p.PixelWidth} /Height {p.PixelHeight} " +
                    $"/ColorSpace {(p.Gray ? "/DeviceGray" : "/DeviceRGB")} /BitsPerComponent 8 /Filter /DCTDecode " +
                    $"/Length {p.Jpeg.Length} >>\nstream\n");
            w.Bytes(p.Jpeg);
            w.Ascii("\nendstream\nendobj\n");
        }

        long xref = w.Position;
        var sb = new StringBuilder();
        sb.Append("xref\n0 ").Append(count + 1).Append('\n');
        sb.Append("0000000000 65535 f \n");
        for (int n = 1; n <= count; n++) sb.Append(offsets[n].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size ").Append(count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        w.Ascii(sb.ToString());
        output.Flush();
    }

    public static byte[] Write(IReadOnlyList<PageImage> pages)
    {
        using var ms = new MemoryStream();
        Write(ms, pages);
        return ms.ToArray();
    }

    private static string Num(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private sealed class CountingWriter(Stream s)
    {
        public long Position { get; private set; }
        public void Ascii(string text) => Bytes(Encoding.Latin1.GetBytes(text));
        public void Bytes(ReadOnlySpan<byte> data) { s.Write(data); Position += data.Length; }
    }
}
