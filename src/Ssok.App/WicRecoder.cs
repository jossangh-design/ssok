using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ssok.Core;
using Ssok.Core.Documents;

namespace Ssok.App;

/// <summary>
/// 문서 속 그림을 윈도우 내장 WIC로 다시 압축한다. 만든 STA 스레드에서만 쓴다.
/// - 휴대폰 사진 회전 정보(EXIF)가 있는 JPEG은 화소를 돌리지 않고 회전 정보를 그대로 옮겨 적는다.
///   (문서 프로그램이 원래 보던 방식 그대로 보이도록)
/// - PDF처럼 색 성분 수가 정해진 경우 그대로 맞춘다 (흑백은 흑백으로).
/// - 한글 문서 속 BMP·GIF·PNG는 투명한 부분이 없으면 JPEG으로 바꿔 크게 줄인다 (MayBecomeJpeg).
/// </summary>
internal sealed class WicRecoder(bool forceGray = false) : IImageRecoder
{
    private const string OrientationQuery = "/app1/{ushort=0}/{ushort=274}";
    private const string OrientationWriteQuery = "/app1/ifd/{ushort=274}";

    /// <summary>그림마다 투명한 곳이 있는지 한 번만 확인해 둔다 (단계마다 다시 확인하지 않도록).</summary>
    private readonly Dictionary<string, bool> _transparent = new();

    public RecodedImage? Recode(DocImage image, int maxLongSide, int jpegQuality)
    {
        try
        {
            return image.Codec switch
            {
                ImageCodec.RawRgb8 or ImageCodec.RawGray8 => FromRaw(image, maxLongSide, jpegQuality),
                ImageCodec.Jpeg or ImageCodec.Png or ImageCodec.Bmp or ImageCodec.Gif => FromEncoded(image, maxLongSide, jpegQuality),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            AppLog.Error("recode", ex);
            return null;
        }
    }

    private RecodedImage? FromEncoded(DocImage image, int maxLongSide, int quality)
    {
        int width, height;
        bool gray, alpha;
        ushort orientation = 1;
        using (var ms = new MemoryStream(image.Data, writable: false))
        {
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (image.Codec == ImageCodec.Jpeg) orientation = Orientation(frame);
            width = frame.PixelWidth;
            height = frame.PixelHeight;
            gray = frame.Format == PixelFormats.Gray8 || frame.Format == PixelFormats.Gray16;
            alpha = HasAlpha(frame.Format);
            if (frame.Format == PixelFormats.Cmyk32 && image.Components == 3) return null;
        }

        double scale = Math.Min(1.0, (double)maxLongSide / Math.Max(width, height));

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(image.Data, writable: false);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        if (scale < 0.999) bmp.DecodePixelWidth = Math.Max(1, (int)Math.Round(width * scale));
        bmp.EndInit();
        bmp.Freeze();

        // 실제로 투명한 곳이 있는지는 그림마다 한 번만 확인한다 (알파 채널이 있어도 전부 불투명한 경우가 많다).
        bool transparent = alpha && IsTransparent(image.Key, bmp);

        // 저장 형식: 정해진 형식이 기본. 한글 문서 속 그림은 투명한 곳이 없으면 JPEG으로 바꾼다.
        var outCodec = image.OutputCodec;
        if (image.MayBecomeJpeg && !transparent) outCodec = ImageCodec.Jpeg;
        if (outCodec == ImageCodec.Png && !transparent) alpha = false;

        // 형식도 크기도 그대로인 PNG는 무손실이라 더 작아지지 않는다.
        if (outCodec == ImageCodec.Png && image.Codec == ImageCodec.Png && scale >= 0.999) return null;

        return Encode(bmp, image, outCodec, quality, gray, alpha && transparent, forceGray, orientation);
    }

    private RecodedImage? FromRaw(DocImage image, int maxLongSide, int quality)
    {
        int comps = image.Codec == ImageCodec.RawRgb8 ? 3 : 1;
        if (image.Data.LongLength != (long)image.Width * image.Height * comps) return null;

        BitmapSource src = BitmapSource.Create(image.Width, image.Height, 96, 96,
            comps == 3 ? PixelFormats.Rgb24 : PixelFormats.Gray8, null, image.Data, image.Width * comps);
        src.Freeze();

        double scale = Math.Min(1.0, (double)maxLongSide / Math.Max(image.Width, image.Height));
        if (scale < 0.999)
        {
            var t = new TransformedBitmap(src, new ScaleTransform(scale, scale));
            t.Freeze();
            src = t;
        }
        return Encode(src, image, image.OutputCodec, quality, gray: comps == 1, alpha: false, forceGray, orientation: 1);
    }

    private static RecodedImage? Encode(BitmapSource src, DocImage image, ImageCodec outCodec, int quality,
                                        bool gray, bool alpha, bool forceGray, ushort orientation)
    {
        BitmapEncoder encoder;
        bool wantGray = false;
        BitmapMetadata? metadata = null;
        if (outCodec == ImageCodec.Jpeg)
        {
            wantGray = forceGray || image.Components == 1 || (image.Components == 0 && gray);
            var format = wantGray ? PixelFormats.Gray8 : PixelFormats.Bgr24;
            if (src.Format != format) src = new FormatConvertedBitmap(src, format, null, 0);
            encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
            if (orientation is > 1 and <= 8)
            {
                // 휴대폰 사진의 회전 정보를 새 JPEG에도 적는다. 적지 못하면 이 그림은 바꾸지 않는다.
                try
                {
                    metadata = new BitmapMetadata("jpg");
                    metadata.SetQuery(OrientationWriteQuery, orientation);
                }
                catch (Exception ex)
                {
                    AppLog.Error("orientation", ex);
                    return null;
                }
            }
        }
        else
        {
            var format = alpha ? PixelFormats.Bgra32 : (gray ? PixelFormats.Gray8 : PixelFormats.Bgr24);
            if (src.Format != format) src = new FormatConvertedBitmap(src, format, null, 0);
            encoder = new PngBitmapEncoder();
        }

        encoder.Frames.Add(BitmapFrame.Create(src, null, metadata, null));
        using var output = new MemoryStream();
        encoder.Save(output);
        return new RecodedImage(output.ToArray(), src.PixelWidth, src.PixelHeight, wantGray, outCodec);
    }

    /// <summary>알파 값이 250 미만인 화소가 하나라도 있으면 투명한 그림으로 본다.</summary>
    private bool IsTransparent(string key, BitmapSource src)
    {
        if (_transparent.TryGetValue(key, out var known)) return known;
        bool transparent = false;
        try
        {
            // 너무 큰 그림은 줄여서 확인한다 (긴 쪽 512px).
            BitmapSource probe = src;
            double s = Math.Min(1.0, 512.0 / Math.Max(src.PixelWidth, src.PixelHeight));
            if (s < 0.999) probe = new TransformedBitmap(src, new ScaleTransform(s, s));
            var bgra = new FormatConvertedBitmap(probe, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
            var pixels = new byte[stride * h];
            bgra.CopyPixels(pixels, stride, 0);
            for (int i = 3; i < pixels.Length; i += 4)
                if (pixels[i] < 250) { transparent = true; break; }
        }
        catch (Exception ex)
        {
            AppLog.Error("alpha-probe", ex);
            transparent = true; // 확인하지 못하면 안전하게 투명한 것으로 보고 PNG로 둔다
        }
        _transparent[key] = transparent;
        return transparent;
    }

    private static ushort Orientation(BitmapFrame frame)
    {
        try
        {
            return frame.Metadata is BitmapMetadata md && md.ContainsQuery(OrientationQuery)
                   && md.GetQuery(OrientationQuery) is ushort o && o is >= 1 and <= 8 ? o : (ushort)1;
        }
        catch { return 1; }
    }

    internal static bool HasAlpha(PixelFormat f) =>
        f == PixelFormats.Bgra32 || f == PixelFormats.Pbgra32 || f == PixelFormats.Rgba64 || f == PixelFormats.Prgba64
        || f == PixelFormats.Rgba128Float || f == PixelFormats.Prgba128Float
        || f == PixelFormats.Indexed1 || f == PixelFormats.Indexed2 || f == PixelFormats.Indexed4 || f == PixelFormats.Indexed8;
}
