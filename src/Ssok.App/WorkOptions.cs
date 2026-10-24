namespace Ssok.App;

internal enum PhotoFormat { Original, Jpg, Png }

/// <summary>파일 하나를 줄일 때 쓰는 설정.</summary>
/// <param name="PdfPagesToImages">그림만 줄여서 목표에 못 미치면 PDF 쪽을 그림으로 바꿔 맞출지. 화면에서는 늘 켜 둔다 (끄는 것은 자가 시험용).</param>
internal sealed record WorkOptions(long TargetBytes, PhotoFormat PhotoFormat, bool RemoveLocation, bool PdfGray,
                                   bool PdfPagesToImages = true);
