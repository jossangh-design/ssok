using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ssok.Core;

namespace Ssok.App;

/// <summary>
/// WPF(윈도우 내장 WIC) 기능으로 사진을 다시 압축한다. 외부 부품을 쓰지 않는다.
/// - 휴대폰 사진의 회전 정보(EXIF Orientation)를 그림에 적용한 뒤 저장한다.
/// - 위치정보 지우기를 켜면 촬영 위치(GPS)·카메라 정보 같은 부가 정보를 넣지 않는다.
///   끄면 JPG 원본의 부가 정보를 그대로 옮긴다 (회전 값만 1로 바꿈).
/// - 투명한 그림을 JPG로 바꿀 때는 흰 바탕을 깐다.
/// 이 클래스의 객체는 만든 스레드에서만 쓴다 (ImageWork.RunSta 참고).
/// </summary>
internal sealed class WpfImage : IEncodableImage
{
    private const string OrientationQuery = "/app1/{ushort=0}/{ushort=274}";
    private const string GpsQuery = "/app1/{ushort=0}/{ushort=34853}";

    private readonly byte[] _original;
    private readonly int _rawWidth;
    private readonly int _orientation;
    private readonly bool _jpeg;
    private readonly bool _hasAlpha;
    private BitmapMetadata? _metadata;

    public int Width { get; }
    public int Height { get; }
    public bool SupportsQuality => _jpeg;

    /// <summary>원본에 촬영 위치(GPS) 정보가 있으면 true.</summary>
    public bool HasLocation { get; }

    private WpfImage(byte[] original, int rawWidth, int rawHeight, int orientation, bool jpeg, bool hasAlpha, bool hasLocation, BitmapMetadata? metadata)
    {
        _original = original;
        _rawWidth = rawWidth;
        _orientation = orientation;
        _jpeg = jpeg;
        _hasAlpha = hasAlpha;
        _metadata = metadata;
        HasLocation = hasLocation;
        bool swapped = orientation is >= 5 and <= 8;
        Width = swapped ? rawHeight : rawWidth;
        Height = swapped ? rawWidth : rawHeight;
    }

    /// <param name="outputJpeg">true면 JPG로, false면 PNG로 저장한다.</param>
    /// <param name="keepMetadata">위치정보 지우기를 끈 경우 true.</param>
    public static WpfImage Load(byte[] fileBytes, bool outputJpeg, bool keepMetadata)
    {
        using var ms = new MemoryStream(fileBytes, writable: false);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new InvalidDataException("그림이 들어 있지 않습니다.");
        var frame = decoder.Frames[0];

        int orientation = 1;
        bool hasLocation = false;
        BitmapMetadata? keep = null;
        try
        {
            if (frame.Metadata is BitmapMetadata md)
            {
                if (md.ContainsQuery(OrientationQuery) && md.GetQuery(OrientationQuery) is ushort o && o is >= 1 and <= 8)
                    orientation = o;
                hasLocation = md.ContainsQuery(GpsQuery);

                if (keepMetadata && outputJpeg && md.Format == "jpg")
                {
                    keep = md.Clone();
                    if (keep.ContainsQuery(OrientationQuery)) keep.SetQuery(OrientationQuery, (ushort)1);
                }
            }
        }
        catch
        {
            // 부가 정보가 깨져 있어도 그림은 처리한다.
            keep = null;
        }

        return new WpfImage(fileBytes, frame.PixelWidth, frame.PixelHeight, orientation, outputJpeg,
                            WicRecoder.HasAlpha(frame.Format), hasLocation, keep);
    }

    public byte[] Encode(double scale, int quality)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(_original, writable: false);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        if (scale < 0.999)
        {
            // 윈도우 그림 해석기가 읽으면서 바로 줄이므로 화질이 좋고 메모리를 덜 쓴다.
            bmp.DecodePixelWidth = Math.Max(1, (int)Math.Round(_rawWidth * scale));
        }
        bmp.EndInit();
        bmp.Freeze();

        BitmapSource src = ApplyOrientation(bmp, _orientation);

        if (_jpeg)
        {
            src = _hasAlpha ? FlattenOnWhite(src)
                : (src.Format == PixelFormats.Bgr24 || src.Format == PixelFormats.Gray8 ? src
                   : new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0));
        }
        else if (_hasAlpha && src.Format != PixelFormats.Bgra32)
        {
            src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        }

        if (_metadata is not null)
        {
            try { return Save(src, quality, _metadata); }
            catch { _metadata = null; /* 부가 정보를 옮기지 못하면 그림만 저장 */ }
        }
        return Save(src, quality, null);
    }

    private byte[] Save(BitmapSource src, int quality, BitmapMetadata? metadata)
    {
        BitmapEncoder encoder = _jpeg
            ? new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) }
            : new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
        encoder.Frames.Add(metadata is null ? BitmapFrame.Create(src) : BitmapFrame.Create(src, null, metadata, null));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>투명한 부분을 흰색으로 채운 컬러 그림을 만든다 (JPG는 투명을 못 담음).</summary>
    private static BitmapSource FlattenOnWhite(BitmapSource src)
    {
        var bgra = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var px = new byte[w * h * 4];
        bgra.CopyPixels(px, w * 4, 0);
        var rgb = new byte[w * h * 3];
        for (int i = 0, j = 0; i < px.Length; i += 4, j += 3)
        {
            int a = px[i + 3];
            rgb[j] = (byte)((px[i] * a + 255 * (255 - a)) / 255);
            rgb[j + 1] = (byte)((px[i + 1] * a + 255 * (255 - a)) / 255);
            rgb[j + 2] = (byte)((px[i + 2] * a + 255 * (255 - a)) / 255);
        }
        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, rgb, w * 3);
        result.Freeze();
        return result;
    }

    private static BitmapSource ApplyOrientation(BitmapSource src, int orientation)
    {
        if (orientation <= 1) return src;

        var group = new TransformGroup();
        switch (orientation)
        {
            case 2: group.Children.Add(new ScaleTransform(-1, 1)); break;
            case 3: group.Children.Add(new RotateTransform(180)); break;
            case 4: group.Children.Add(new ScaleTransform(1, -1)); break;
            case 5: group.Children.Add(new ScaleTransform(-1, 1)); group.Children.Add(new RotateTransform(270)); break;
            case 6: group.Children.Add(new RotateTransform(90)); break;
            case 7: group.Children.Add(new ScaleTransform(-1, 1)); group.Children.Add(new RotateTransform(90)); break;
            case 8: group.Children.Add(new RotateTransform(270)); break;
            default: return src;
        }
        var t = new TransformedBitmap(src, group);
        t.Freeze();
        return t;
    }
}
