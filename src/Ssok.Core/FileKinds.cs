namespace Ssok.Core;

public enum FileKind { Unsupported, Jpeg, Png, Heic, Webp, Bmp, Pdf, Hwp, Hwpx }

public enum FileGroup { None, Photo, Pdf, Hangul }

public static class FileKinds
{
    public static readonly string[] Extensions = { ".pdf", ".hwp", ".hwpx", ".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".bmp" };

    public static FileKind Detect(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => FileKind.Jpeg,
            ".png" => FileKind.Png,
            ".heic" or ".heif" => FileKind.Heic,
            ".webp" => FileKind.Webp,
            ".bmp" => FileKind.Bmp,
            ".pdf" => FileKind.Pdf,
            ".hwp" => FileKind.Hwp,
            ".hwpx" => FileKind.Hwpx,
            _ => FileKind.Unsupported,
        };
    }

    /// <summary>지금 버전에서 실제로 줄일 수 있는 종류.</summary>
    public static bool IsReadyNow(FileKind kind) => kind != FileKind.Unsupported;

    public static FileGroup Group(FileKind kind) => kind switch
    {
        FileKind.Pdf => FileGroup.Pdf,
        FileKind.Hwp or FileKind.Hwpx => FileGroup.Hangul,
        FileKind.Unsupported => FileGroup.None,
        _ => FileGroup.Photo,
    };

    public static bool IsPhoto(FileKind kind) => Group(kind) == FileGroup.Photo;

    public static string DisplayName(FileKind kind) => kind switch
    {
        FileKind.Jpeg => "JPG 사진",
        FileKind.Png => "PNG 그림",
        FileKind.Heic => "HEIC 사진",
        FileKind.Webp => "WebP 그림",
        FileKind.Bmp => "BMP 그림",
        FileKind.Pdf => "PDF",
        FileKind.Hwp => "hwp 문서",
        FileKind.Hwpx => "hwpx 문서",
        _ => "지원하지 않는 파일",
    };
}
