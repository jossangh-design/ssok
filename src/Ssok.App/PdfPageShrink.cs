using Ssok.Core.Documents;

namespace Ssok.App;

/// <summary>
/// 그림만 줄여서는 목표에 못 미치는 PDF를, 쪽마다 그림(JPEG)으로 바꿔 목표에 맞춘다 (쏙 2.0 방식).
/// 글자도 그림이 되므로 검색·복사가 안 되지만, 어떤 PDF든 목표에 맞출 수 있다.
/// </summary>
internal static class PdfPageShrink
{
    private const int SamplePages = 6;

    public sealed record Outcome(byte[] Data, int Dpi, int Quality, bool Reached, int Pages);

    /// <param name="progress">(시도 번호, 이번 시도 안에서 0~1 진행)</param>
    /// <param name="force">true면 알아보기 어려울 만큼 낮은 단계까지 쓴다 (사용자가 고른 경우).</param>
    public static Outcome Run(string path, long targetBytes, bool gray, Action<int, double>? progress, CancellationToken cancel,
                              bool force = false)
    {
        var ladder = RasterSearch.LadderFor(force);
        using var raster = PdfRasterizer.Open(path);
        int n = raster.PageCount;
        if (n == 0) throw new DocumentNotSupportedException("쪽이 없는 PDF예요.");
        var sizes = Enumerable.Range(0, n).Select(raster.PageSize).ToArray();

        // 어림값: 고르게 뽑은 몇 쪽만 그려서 전체 크기를 짐작한다.
        var sample = Enumerable.Range(0, Math.Min(n, SamplePages))
            .Select(k => (int)((long)k * n / Math.Min(n, SamplePages))).Distinct().ToArray();
        var estimates = new Dictionary<int, long>();
        int attemptNow = 0;
        long Estimate(int level)
        {
            if (estimates.TryGetValue(level, out var e)) return e;
            var (dpi, q) = ladder[level];
            long sum = 0;
            foreach (var i in sample)
            {
                cancel.ThrowIfCancellationRequested();
                sum += raster.RenderJpeg(i, dpi, q, gray).Jpeg.LongLength;
            }
            // 쪽마다 붙는 PDF 구조 몇백 바이트를 더한다.
            e = sum * n / sample.Length + n * 400L + 1024;
            estimates[level] = e;
            progress?.Invoke(Math.Max(1, attemptNow), 0);
            return e;
        }

        byte[] Build(int level)
        {
            var (dpi, q) = ladder[level];
            var pages = new List<PageImage>(n);
            for (int i = 0; i < n; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var (jpeg, w, h) = raster.RenderJpeg(i, dpi, q, gray);
                pages.Add(new PageImage(jpeg, w, h, sizes[i].WidthPt, sizes[i].HeightPt, gray));
                progress?.Invoke(attemptNow, (i + 1.0) / n);
            }
            return PdfImageWriter.Write(pages);
        }

        var r = RasterSearch.FindWithEstimate(Build, Estimate, targetBytes,
            (attempt, _) => { attemptNow = attempt; progress?.Invoke(attempt, 0); }, cancel, ladder);
        var (d, quality) = ladder[r.Level];
        return new Outcome(r.Data, d, quality, r.Reached, n);
    }
}
