namespace Ssok.App;

internal enum PhotoFormat { Original, Jpg, Png }

/// <summary>파일 하나를 줄일 때 쓰는 설정.</summary>
/// <param name="PdfPagesToImages">그림만 줄여서 목표에 못 미치면 PDF 쪽을 그림으로 바꿔 맞출지. 화면에서는 늘 켜 둔다 (끄는 것은 자가 시험용).</param>
/// <param name="Force">글자·사진을 알아보기 어려울 만큼 줄여서라도 목표에 맞출지. 사용자가 창에서 고른 경우에만 켠다.</param>
internal sealed record WorkOptions(long TargetBytes, PhotoFormat PhotoFormat, bool RemoveLocation, bool PdfGray,
                                   bool PdfPagesToImages = true, bool Force = false);
