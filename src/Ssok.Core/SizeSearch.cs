namespace Ssok.Core;

/// <summary>다시 압축할 수 있는 그림 하나. 화면 기술(WPF 등)과 무관하게 탐색 로직을 시험하려고 분리했다.</summary>
public interface IEncodableImage
{
    int Width { get; }
    int Height { get; }

    /// <summary>JPG처럼 품질을 고를 수 있으면 true, PNG처럼 무손실이면 false.</summary>
    bool SupportsQuality { get; }

    /// <param name="scale">0~1. 1이면 원래 크기.</param>
    /// <param name="quality">1~100. SupportsQuality가 false면 무시된다.</param>
    byte[] Encode(double scale, int quality);
}

public sealed record ShrinkOptions
{
    public required long TargetBytes { get; init; }

    /// <summary>이보다 낮은 품질은 글자가 뭉개져서 쓰지 않는다. 대신 크기를 줄인다.</summary>
    public int MinQuality { get; init; } = 50;
    public int MaxQuality { get; init; } = 100;

    /// <summary>한 번에 줄이는 비율. 0.85면 가로·세로를 15%씩 줄여 가며 찾는다.</summary>
    public double ScaleStep { get; init; } = 0.85;

    /// <summary>짧은 쪽이 이보다 작아지면 더 줄이지 않는다 (알아볼 수 없는 그림 방지).</summary>
    public int MinShortSidePx { get; init; } = 240;

    /// <summary>사용자가 '멈추기'를 누르면 다음 시도 전에 멈춘다.</summary>
    public CancellationToken Cancel { get; init; }

    /// <summary>새로 압축해 볼 때마다 (시도 번호).</summary>
    public Action<int>? OnAttempt { get; init; }
}

public enum ShrinkStatus
{
    /// <summary>목표 용량 이하로 만들었다.</summary>
    Done,
    /// <summary>원본이 이미 목표 이하다. 새 파일을 만들지 않는다.</summary>
    AlreadySmall,
    /// <summary>최소 품질·최소 크기까지 줄여도 목표에 닿지 않는다.</summary>
    CannotReach,
}

public sealed record ShrinkResult(ShrinkStatus Status, byte[]? Data, int Quality, double Scale, int Attempts);

public static class SizeSearch
{
    /// <summary>
    /// 목표 용량 이하가 되는 결과 중 가장 화질이 좋은 것을 찾는다.
    /// 순서: 원래 크기에서 품질을 낮춰 보고 → 최소 품질로도 안 되면 크기를 조금씩 줄인다.
    /// </summary>
    public static ShrinkResult Find(IEncodableImage image, long originalBytes, ShrinkOptions options)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);
        if (options.TargetBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options), "목표 용량은 0보다 커야 합니다.");

        if (originalBytes <= options.TargetBytes)
            return new ShrinkResult(ShrinkStatus.AlreadySmall, null, 0, 1, 0);

        int attempts = 0;
        int shortSide = Math.Min(image.Width, image.Height);
        double scale = 1.0;

        while (true)
        {
            long smallestAtThisScale;
            var found = image.SupportsQuality
                ? BestQualityAt(image, scale, options, ref attempts, out smallestAtThisScale)
                : TryLossless(image, scale, options, ref attempts, out smallestAtThisScale);

            if (found is { } hit)
                return new ShrinkResult(ShrinkStatus.Done, hit.Data, hit.Quality, scale, attempts);

            // 용량은 대략 (가로×세로)에 비례하므로 필요한 비율을 어림해서 한 번에 건너뛴다.
            // 어림이 빗나가도 ScaleStep보다 덜 줄이지는 않으므로 반드시 끝난다.
            double estimate = Math.Sqrt((double)options.TargetBytes / Math.Max(1, smallestAtThisScale)) * 0.97;
            double next = scale * Math.Min(options.ScaleStep, estimate);
            // 원본이 이미 작은 그림이면 처음부터 최소 크기 규칙을 적용하지 않고, 원본의 절반까지만 허용한다.
            double floor = shortSide > options.MinShortSidePx
                ? (double)options.MinShortSidePx / shortSide
                : 0.5;
            if (next < floor)
                return new ShrinkResult(ShrinkStatus.CannotReach, null, 0, scale, attempts);
            scale = next;
        }
    }

    private static (byte[] Data, int Quality)? BestQualityAt(IEncodableImage image, double scale, ShrinkOptions o, ref int attempts, out long smallest)
    {
        attempts++;
        o.Cancel.ThrowIfCancellationRequested();
        o.OnAttempt?.Invoke(attempts);
        var top = image.Encode(scale, o.MaxQuality);
        smallest = top.Length;
        if (top.Length <= o.TargetBytes) return (top, o.MaxQuality);

        attempts++;
        o.Cancel.ThrowIfCancellationRequested();
        o.OnAttempt?.Invoke(attempts);
        var bottom = image.Encode(scale, o.MinQuality);
        smallest = bottom.Length;
        if (bottom.Length > o.TargetBytes) return null;

        // bottom(lo)은 목표 이하, top(hi)은 초과. 그 사이에서 목표 이하인 가장 높은 품질을 찾는다.
        int lo = o.MinQuality, hi = o.MaxQuality;
        byte[] best = bottom;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            attempts++;
            o.Cancel.ThrowIfCancellationRequested();
            o.OnAttempt?.Invoke(attempts);
            var data = image.Encode(scale, mid);
            if (data.Length <= o.TargetBytes) { lo = mid; best = data; }
            else hi = mid;
        }
        return (best, lo);
    }

    private static (byte[] Data, int Quality)? TryLossless(IEncodableImage image, double scale, ShrinkOptions o, ref int attempts, out long smallest)
    {
        attempts++;
        o.Cancel.ThrowIfCancellationRequested();
        o.OnAttempt?.Invoke(attempts);
        var data = image.Encode(scale, 100);
        smallest = data.Length;
        return data.Length <= o.TargetBytes ? (data, 100) : null;
    }
}
