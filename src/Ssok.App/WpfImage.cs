using System.Collections.ObjectModel;
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
/// - 색 정보(ICC 프로필)는 어느 쪽이든 옮긴다 (빠지면 넓은 색 영역 사진이 바래 보인다).
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
    private readonly bool _sourceGray;
    private BitmapMetadata? _metadata;
    /// <summary>원본의 색 정보(ICC 프로필). 아이폰 사진(Display-P3)처럼 넓은 색 영역 사진이 바래 보이지 않게 그대로 옮긴다.</summary>
    private ReadOnlyCollection<ColorContext>? _colorContexts;

    public int Width { get; }
    public int Height { get; }
    public bool SupportsQuality => _jpeg;

    /// <summary>원본에 촬영 위치(GPS) 정보가 있으면 true.</summary>
    public bool HasLocation { get; }

    private WpfImage(byte[] original, int rawWidth, int rawHeight, int orientation, bool jpeg, bool hasAlpha, bool hasLocation,
                     BitmapMetadata? metadata, ReadOnlyCollection<ColorContext>? colorContexts, bool sourceGray)
    {
        _original = original;
        _rawWidth = rawWidth;
        _orientation = orientation;
        _jpeg = jpeg;
        _hasAlpha = hasAlpha;
        _metadata = metadata;
        _colorContexts = colorContexts;
        _sourceGray = sourceGray;
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

        // 색 정보는 위치정보가 아니므로 '위치정보 지우기'를 켜도 옮긴다.
        ReadOnlyCollection<ColorContext>? colors = null;
        try { colors = frame.ColorContexts is { Count: > 0 } c ? c : null; }
        catch { /* 색 정보를 못 읽는 형식: 그림만 저장 */ }

        bool sourceGray = frame.Format == PixelFormats.Gray8 || frame.Format == PixelFormats.Gray16;
        return new WpfImage(fileBytes, frame.PixelWidth, frame.PixelHeight, orientation, outputJpeg,
                            WicRecoder.HasAlpha(frame.Format), hasLocation, keep, colors, sourceGray);
    }

    /// <summary>마지막으로 연 크기와 그 그림. 같은 크기에서 품질만 바꿔 볼 때는 다시 열지 않는다.</summary>
    private double _preparedScale = -1;
    private BitmapSource? _prepared;

    /// <summary>원본을 연(해석한) 횟수. 자가 시험에서 다시 쓰기가 되는지 확인할 때 쓴다.</summary>
    internal int Decodes { get; private set; }

    public byte[] Encode(double scale, int quality)
    {
        // 품질을 찾는 동안은 크기가 같으므로, 열고 돌리고 형식을 바꾼 그림을 그대로 다시 쓴다.
        // 메모리를 아끼려고 마지막 한 크기만 기억한다.
        if (_prepared is null || Math.Abs(_preparedScale - scale) > 1e-9)
        {
            _prepared = null; // 새 그림을 만들기 전에 이전 그림을 놓아준다
            _prepared = Prepare(scale);
            _preparedScale = scale;
        }
        var src = _prepared;

        // 컬러 원본의 색 정보를 흑백 결과에 붙이면 색이 틀어지므로, 색 성분이 같을 때만 옮긴다.
        var colors = _colorContexts is not null && (src.Format != PixelFormats.Gray8 || _sourceGray) ? _colorContexts : null;
        if (_metadata is not null)
        {
            try { return Save(src, quality, _metadata, colors); }
            catch { _metadata = null; /* 부가 정보를 옮기지 못하면 색 정보·그림만 저장 */ }
        }
        if (colors is not null)
        {
            try { return Save(src, quality, null, colors); }
            catch { _colorContexts = null; /* 색 정보를 옮기지 못하면 그림만 저장 */ }
        }
        return Save(src, quality, null, null);
    }

    /// <summary>원본을 scale 크기로 열고, 회전·형식 바꾸기까지 마친 화소를 메모리에 만들어 둔다.</summary>
    private BitmapSource Prepare(double scale)
    {
        Decodes++;
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

        // 돌리기·형식 바꾸기는 저장할 때마다 다시 계산되므로, 결과 화소를 한 번 복사해 둔다.
        try
        {
            var ready = new WriteableBitmap(src);
            ready.Freeze();
            return ready;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
        {
            return src; // 복사할 수 없는 화소 형식: 그대로 쓴다 (느리지만 결과는 같다)
        }
    }

    private byte[] Save(BitmapSource src, int quality, BitmapMetadata? metadata, ReadOnlyCollection<ColorContext>? colors)
    {
        BitmapEncoder encoder = _jpeg
            ? new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) }
            : new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
        encoder.Frames.Add(metadata is null && colors is null ? BitmapFrame.Create(src) : BitmapFrame.Create(src, null, metadata, colors));
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
