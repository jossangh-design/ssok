namespace Ssok.Core.Documents;

/// <summary>
/// 쪽을 그림으로 바꿔 줄이는 방식에서, 목표 용량 안에 드는 가장 선명한 단계를 찾는다.
/// 단계가 뒤로 갈수록 해상도(dpi)와 화질이 낮아져 파일이 작아진다.
/// </summary>
public static class RasterSearch
{
    /// <summary>(dpi, JPEG 화질). 쏙 2.0 단계표에 더 작은 단계를 두 개 더했다.</summary>
    public static readonly IReadOnlyList<(int Dpi, int Quality)> Ladder = new[]
    {
        (200, 85), (170, 80), (150, 75), (135, 70), (120, 65), (110, 60), (96, 55),
        (85, 50), (75, 45), (66, 40), (58, 35), (50, 30), (40, 27),
        // 쪽이 아주 많은 PDF도 목표에 맞추기 위한 마지막 단계
        (34, 25), (30, 22),
    };

    public sealed record Result(byte[] Data, int Level, bool Reached, int Attempts);

    /// <param name="build">단계 번호를 받아 그 단계로 만든 PDF를 돌려준다.</param>
    /// <param name="onAttempt">새 단계를 만들기 시작할 때 (시도 번호, 단계 번호).</param>
    public static Result Find(Func<int, byte[]> build, long targetBytes, Action<int, int>? onAttempt = null,
                              CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        var made = new Dictionary<int, byte[]>();
        int attempts = 0;

        byte[] Make(int level)
        {
            if (made.TryGetValue(level, out var d)) return d;
            cancel.ThrowIfCancellationRequested();
            attempts++;
            onAttempt?.Invoke(attempts, level);
            d = build(level);
            made[level] = d;
            return d;
        }

        // 목표 이하인 가장 앞(선명한) 단계를 이분 탐색으로 찾는다.
        int lo = 0, hi = Ladder.Count - 1, best = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Make(mid).LongLength <= targetBytes) { best = mid; hi = mid - 1; }
            else lo = mid + 1;
        }

        if (best >= 0) return new Result(made[best], best, true, attempts);
        int last = Ladder.Count - 1;
        return new Result(Make(last), last, false, attempts);
    }

    /// <summary>
    /// 몇 쪽만 그려 본 어림값으로 시작 단계를 고른 뒤, 전체를 만들어 확인한다. 보통 1~2번이면 끝난다.
    /// 어림이 빗나가면 한 단계씩 옮겨 가며 다시 만든다.
    /// </summary>
    /// <param name="estimate">단계 번호를 받아 예상 파일 크기를 돌려준다 (전체를 만드는 것보다 훨씬 빨라야 한다).</param>
    public static Result FindWithEstimate(Func<int, byte[]> build, Func<int, long> estimate, long targetBytes,
                                          Action<int, int>? onAttempt = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(estimate);
        int last = Ladder.Count - 1;
        var made = new Dictionary<int, byte[]>();
        int attempts = 0;

        byte[] Make(int level)
        {
            if (made.TryGetValue(level, out var d)) return d;
            cancel.ThrowIfCancellationRequested();
            attempts++;
            onAttempt?.Invoke(attempts, level);
            d = build(level);
            made[level] = d;
            return d;
        }

        // 어림값이 목표의 97% 이하인 가장 선명한 단계 (어림값도 단계가 뒤로 갈수록 작아진다고 본다)
        long safe = (long)(targetBytes * 0.97);
        int lo = 0, hi = last, start = last;
        while (lo <= hi)
        {
            cancel.ThrowIfCancellationRequested();
            int mid = (lo + hi) / 2;
            if (estimate(mid) <= safe) { start = mid; hi = mid - 1; }
            else lo = mid + 1;
        }

        int level = start;
        if (Make(level).LongLength <= targetBytes)
        {
            // 들어맞았으면 한 단계 더 선명하게 해 볼 만한지 확인한다.
            while (level > 0 && estimate(level - 1) <= targetBytes * 1.05 && Make(level - 1).LongLength <= targetBytes)
                level--;
            return new Result(made[level], level, true, attempts);
        }
        while (level < last)
        {
            level++;
            if (Make(level).LongLength <= targetBytes) return new Result(made[level], level, true, attempts);
        }
        return new Result(made[last], last, false, attempts);
    }
}
