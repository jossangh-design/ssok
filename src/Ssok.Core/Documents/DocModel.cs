namespace Ssok.Core.Documents;

public enum ImageCodec
{
    Jpeg,
    Png,
    Bmp,
    Gif,
    /// <summary>PDF 안의 압축 안 된 RGB 화소 (가로×세로×3 바이트).</summary>
    RawRgb8,
    /// <summary>PDF 안의 압축 안 된 흑백 화소 (가로×세로 바이트).</summary>
    RawGray8,
    Unknown,
}

/// <summary>문서 안에 들어 있는 그림 하나.</summary>
public sealed class DocImage
{
    /// <summary>문서 형식마다 그림을 가리키는 이름 (zip 항목 이름, 스트림 이름, PDF 객체 번호).</summary>
    public required string Key { get; init; }
    public required ImageCodec Codec { get; init; }
    public required byte[] Data { get; init; }

    /// <summary>바꿔 넣을 때 써야 하는 형식. hwp·hwpx는 원래 형식을 유지해야 하고, PDF는 JPEG로 바꿀 수 있다.</summary>
    public required ImageCodec OutputCodec { get; init; }

    /// <summary>Raw 형식일 때만 필요. 나머지는 0이어도 된다.</summary>
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>색 성분 수 (흑백 1, 컬러 3). 0이면 상관없음. PDF처럼 색 공간이 정해진 곳에서 맞춰야 한다.</summary>
    public int Components { get; init; }

    /// <summary>
    /// 투명한 부분이 없으면 JPEG으로 바꿔 저장해도 되는지 (한글 문서 속 BMP·GIF·PNG).
    /// 바꾸면 문서 쪽에서 그림 이름(확장자)도 함께 고친다.
    /// </summary>
    public bool MayBecomeJpeg { get; init; }
}

/// <param name="Gray">흑백으로 바꿔 저장했으면 true (PDF 색 공간을 함께 바꾼다).</param>
/// <param name="Codec">실제로 저장한 형식. Unknown이면 원래 정해진 형식(OutputCodec) 그대로.</param>
public sealed record RecodedImage(byte[] Data, int Width, int Height, bool Gray = false, ImageCodec Codec = ImageCodec.Unknown)
{
    /// <summary>이 결과의 실제 형식.</summary>
    public ImageCodec CodecFor(DocImage source) => Codec == ImageCodec.Unknown ? source.OutputCodec : Codec;

    /// <summary>형식에 맞는 확장자 (점 없이, 세 글자).</summary>
    public static string ExtensionOf(ImageCodec codec) => codec switch
    {
        ImageCodec.Jpeg => "jpg",
        ImageCodec.Png => "png",
        ImageCodec.Bmp => "bmp",
        ImageCodec.Gif => "gif",
        _ => "bin",
    };
}

/// <summary>그림을 다시 압축하는 기능. 윈도우에서는 WIC로 구현한다 (Ssok.App).</summary>
public interface IImageRecoder
{
    /// <summary>
    /// 긴 쪽이 maxLongSide를 넘으면 줄이고, JPEG면 quality로 저장한다.
    /// 처리할 수 없거나 결과가 원본보다 크면 null.
    /// </summary>
    RecodedImage? Recode(DocImage image, int maxLongSide, int jpegQuality);
}

/// <summary>그림을 바꿔 끼울 수 있는 문서 하나.</summary>
public interface IDocumentPackage : IDisposable
{
    long OriginalSize { get; }
    IReadOnlyList<DocImage> Images { get; }

    /// <summary>바꿀 그림만 넣은 표로 새 문서를 만든다. 표에 없는 그림은 그대로 둔다.</summary>
    /// <param name="cancel">'멈추기'를 누르면 만들던 중이라도 멈춘다 (PDF처럼 오래 걸리는 형식).</param>
    byte[] Build(IReadOnlyDictionary<string, RecodedImage> replacements, CancellationToken cancel = default);
}

/// <summary>사용자에게 그대로 보여줄 수 있는 문장을 담은 예외. 경로·파일 이름은 넣지 않는다.</summary>
public sealed class DocumentNotSupportedException(string userMessage) : Exception(userMessage)
{
    public string UserMessage { get; } = userMessage;
}

public static class ImageSniffer
{
    public static ImageCodec Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ImageCodec.Jpeg;
        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ImageCodec.Png;
        if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M') return ImageCodec.Bmp;
        if (data.Length >= 4 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'8') return ImageCodec.Gif;
        return ImageCodec.Unknown;
    }
}
