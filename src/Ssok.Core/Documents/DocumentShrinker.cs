namespace Ssok.Core.Documents;

public enum DocShrinkStatus
{
    /// <summary>목표 이하로 만들었다.</summary>
    Done,
    /// <summary>원본이 이미 목표 이하.</summary>
    AlreadySmall,
    /// <summary>가장 많이 줄여도 목표에 못 미쳤지만, 원본보다는 작아졌다. Data에 그 결과가 들어 있다.</summary>
    BestEffort,
    /// <summary>줄일 수 있는 그림이 없거나, 줄여도 작아지지 않는다.</summary>
    NothingToShrink,
}

public sealed record DocShrinkResult(DocShrinkStatus Status, byte[]? Data, int Level, int ImagesChanged, int ImagesTotal, int Builds)
{
    /// <summary>고른 단계에서 바꿔 끼운 그림들 (비교 화면용).</summary>
    public IReadOnlyDictionary<string, RecodedImage>? Replacements { get; init; }

    /// <summary>가장 큰 그림에 적용한 단계 (긴 쪽 최대 픽셀, JPEG 품질). 화면 안내용.</summary>
    public int MaxLongSide { get; init; }
    public int Quality { get; init; }
}

/// <summary>
/// 문서 속 그림에 단계를 적용하면서, 목표 용량 이하가 되는 가장 약한 단계를 찾는다.
/// 글자·표·서식은 건드리지 않고 그림만 바꾼다.
/// 큰 그림부터 줄인다: 문서 그림 전체에서 차지하는 몫이 작은 그림(도표·아이콘 등)은 몇 단계 약하게 줄여
/// 같은 용량에서 더 선명하게 남긴다. 가장 센 단계에서는 모든 그림을 똑같이 줄이므로 맞출 수 있는 용량은 그대로다.
/// </summary>
public static class DocumentShrinker
{
    /// <summary>단계가 올라갈수록 많이 줄인다. (긴 쪽 최대 픽셀, JPEG 품질)</summary>
    public static readonly IReadOnlyList<(int MaxLongSide, int Quality)> Ladder = new[]
    {
        (4096, 88),
        (3000, 85),
        (2400, 82),
        (2000, 80),
        (1600, 78),
        (1600, 70),
        (1280, 70),
        (1280, 62),
        (1024, 62),
        (1024, 55),
        (800, 55),
        (800, 50),
        (640, 50),
        // 목표에 꼭 맞추기 위한 마지막 단계들 (그림이 아주 많은 문서용)
        (520, 48),
        (400, 45),
        (320, 40),
    };

    /// <summary>사용자가 '알아보기 어려워도 목표에 맞추기'를 고른 경우에만 더 쓰는 단계.</summary>
    public static readonly IReadOnlyList<(int MaxLongSide, int Quality)> ExtraLadder = new[]
    {
        (240, 35), (200, 30), (160, 25), (120, 20), (96, 15), (64, 10),
    };

    public static IReadOnlyList<(int MaxLongSide, int Quality)> LadderFor(bool force)
        => force ? Ladder.Concat(ExtraLadder).ToArray() : Ladder;

    /// <summary>그림 전체에서 차지하는 몫이 작을수록 몇 단계 약하게 줄일지 (0~3).</summary>
    public static int LighterBy(long imageBytes, long totalImageBytes)
    {
        double share = (double)imageBytes / Math.Max(1, totalImageBytes);
        return share < 0.02 ? 3 : share < 0.05 ? 2 : share < 0.15 ? 1 : 0;
    }

    /// <param name="progress">(시도 번호, 이번 시도 안에서 0~1 진행)</param>
    /// <param name="force">true면 알아보기 어려울 만큼 센 단계까지 쓴다 (사용자가 고른 경우).</param>
    public static DocShrinkResult Shrink(IDocumentPackage package, IImageRecoder recoder, long targetBytes, CancellationToken cancel = default,
                                         Action<int, double>? progress = null, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(recoder);

        int total = package.Images.Count;
        if (package.OriginalSize <= targetBytes)
            return new DocShrinkResult(DocShrinkStatus.AlreadySmall, null, -1, 0, total, 0);
        if (total == 0)
            return new DocShrinkResult(DocShrinkStatus.NothingToShrink, null, -1, 0, 0, 0);

        var ladder = LadderFor(force);
        int last = ladder.Count - 1;
        long imageBytes = package.Images.Sum(i => i.Data.LongLength);
        var lighter = package.Images.ToDictionary(i => i.Key, i => LighterBy(i.Data.LongLength, imageBytes));
        // 단계 번호: 0 ~ last + (가장 많이 늦춘 단계 수). 그림마다 실제 단계 = 단계 번호 - 늦출 단계 수.
        int top = last + lighter.Values.DefaultIfEmpty(0).Max();

        var cache = new Dictionary<(int Level, string Key), RecodedImage?>();
        int builds = 0;

        Dictionary<string, RecodedImage> ReplacementsFor(int level)
        {
            var map = new Dictionary<string, RecodedImage>();
            int done = 0;
            foreach (var img in package.Images)
            {
                cancel.ThrowIfCancellationRequested();
                // 그림 다시 압축이 시간의 대부분, 마지막 10%는 문서 다시 묶기
                progress?.Invoke(builds + 1, 0.9 * done++ / total);
                int own = Math.Clamp(level - lighter[img.Key], 0, last);
                if (!cache.TryGetValue((own, img.Key), out var r))
                {
                    var (side, quality) = ladder[own];
                    r = recoder.Recode(img, side, quality);
                    // 결과가 원본 이상이면 바꾸지 않는다.
                    if (r is not null && r.Data.Length >= img.Data.Length) r = null;
                    cache[(own, img.Key)] = r;
                }
                if (r is not null) map[img.Key] = r;
            }
            return map;
        }

        (byte[] Data, int Changed, Dictionary<string, RecodedImage> Map) Evaluate(int level)
        {
            var map = ReplacementsFor(level);
            progress?.Invoke(builds + 1, 0.9);
            cancel.ThrowIfCancellationRequested();
            var data = package.Build(map, cancel);
            builds++;
            progress?.Invoke(builds, 1.0);
            return (data, map.Count, map);
        }

        DocShrinkResult Result(DocShrinkStatus status, byte[]? data, int level, int changed, Dictionary<string, RecodedImage>? map)
        {
            int shown = Math.Min(level, last);
            return new DocShrinkResult(status, data, shown, changed, total, builds)
                { Replacements = map, MaxLongSide = ladder[shown].MaxLongSide, Quality = ladder[shown].Quality };
        }

        // 가장 많이 줄인 단계부터 확인: 여기서도 안 되면 더 볼 필요가 없다.
        var strongest = Evaluate(top);
        if (strongest.Changed == 0 || strongest.Data.Length >= package.OriginalSize)
            return Result(DocShrinkStatus.NothingToShrink, null, top, 0, null);
        if (strongest.Data.Length > targetBytes)
            return Result(DocShrinkStatus.BestEffort, strongest.Data, top, strongest.Changed, strongest.Map);

        // lo: 목표 초과로 확인된 단계 (-1 = 원본), hi: 목표 이하로 확인된 단계
        int lo = -1, hi = top;
        var best = strongest;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            var attempt = Evaluate(mid);
            if (attempt.Data.Length <= targetBytes) { hi = mid; best = attempt; }
            else lo = mid;
        }
        return Result(DocShrinkStatus.Done, best.Data, hi, best.Changed, best.Map);
    }
}
