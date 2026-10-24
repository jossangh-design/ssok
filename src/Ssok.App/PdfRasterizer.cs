using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Ssok.App;

/// <summary>
/// 윈도우에 내장된 PDF 렌더러(Windows.Data.Pdf)로 PDF 쪽을 그림으로 그린다.
/// 별도 부품을 함께 배포하지 않는다. 윈도우 비동기 기능은 스레드 풀에서 돌리고 결과만 받아 온다.
/// </summary>
internal sealed class PdfRasterizer : IDisposable
{
    private readonly PdfDocument _doc;

    private PdfRasterizer(PdfDocument doc) => _doc = doc;

    public int PageCount => (int)_doc.PageCount;

    public static PdfRasterizer Open(string path)
    {
        var full = Path.GetFullPath(path);
        var doc = Run(async () =>
        {
            var file = await StorageFile.GetFileFromPathAsync(full);
            return await PdfDocument.LoadFromFileAsync(file);
        });
        return new PdfRasterizer(doc);
    }

    /// <summary>쪽 크기 (pt, 1/72인치). 회전이 반영된 크기다.</summary>
    public (double WidthPt, double HeightPt) PageSize(int index)
    {
        using var page = _doc.GetPage((uint)index);
        // Size는 1/96인치 단위
        return (page.Size.Width * 72.0 / 96.0, page.Size.Height * 72.0 / 96.0);
    }

    /// <summary>쪽 하나를 흰 바탕 그림으로 그린다. 긴 쪽은 maxPixels를 넘지 않게 한다.</summary>
    public BitmapSource Render(int index, double dpi, int maxPixels = 6000)
    {
        byte[] bmp = Run(async () =>
        {
            using var page = _doc.GetPage((uint)index);
            double w = page.Size.Width * dpi / 96.0, h = page.Size.Height * dpi / 96.0;
            double fit = Math.Min(1.0, maxPixels / Math.Max(w, h));
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(w * fit)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(h * fit)),
                BackgroundColor = new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 },
                BitmapEncoderId = Windows.Graphics.Imaging.BitmapEncoder.BmpEncoderId, // 빠르고 손실 없음
            };
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, options);
            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
            return bytes;
        });

        var frame = BitmapFrame.Create(new MemoryStream(bmp), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        // 투명 칸이 있으면 JPEG에서 검게 나오므로 24비트로 바꾼다 (바탕은 이미 흰색).
        BitmapSource src = frame.Format == PixelFormats.Bgr24 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);
        src.Freeze();
        return src;
    }

    /// <summary>쪽 하나를 JPEG으로 만든다.</summary>
    public (byte[] Jpeg, int Width, int Height) RenderJpeg(int index, double dpi, int quality, bool gray)
    {
        BitmapSource src = Render(index, dpi);
        if (gray) src = new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0);
        var enc = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return (ms.ToArray(), src.PixelWidth, src.PixelHeight);
    }

    private static T Run<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();

    public void Dispose() { /* PdfDocument는 따로 닫을 필요가 없다 */ }
}
