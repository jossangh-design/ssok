namespace Ssok.Core;

/// <summary>
/// "모두 합쳐서" 목표를 파일마다 나눈다. 원래 크기에 비례해서 나눈다.
/// 화면에서는 파일을 하나 끝낼 때마다 남은 예산으로 다시 나눠서,
/// 덜 줄어든 파일이 있으면 뒤 파일이 그만큼 더 줄도록 한다.
/// </summary>
public static class TargetPlanner
{
    /// <summary>
    /// 파일들을 ZIP 하나(압축 안 함)로 묶을 때 파일 크기 합보다 늘어나는 양.
    /// 항목마다 머리말 두 개(30·46바이트)와 이름(UTF-8) 두 번, 끝 기록 22바이트. 항목마다 64바이트를 넉넉히 더 잡는다.
    /// </summary>
    public static long ZipOverhead(IEnumerable<string> entryNames)
    {
        ArgumentNullException.ThrowIfNull(entryNames);
        return 22 + entryNames.Sum(n => 30 + 46 + 64 + 2L * System.Text.Encoding.UTF8.GetByteCount(n));
    }

    public static long[] Split(long totalTarget, IReadOnlyList<long> sizes)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        var result = new long[sizes.Count];
        long sum = sizes.Where(s => s > 0).Sum();
        if (sum == 0) return result;

        // 예산이 넉넉하면 모두 원래 크기 그대로 (줄일 필요 없음)
        if (totalTarget >= sum)
        {
            for (int i = 0; i < sizes.Count; i++) result[i] = sizes[i];
            return result;
        }

        for (int i = 0; i < sizes.Count; i++)
            result[i] = sizes[i] <= 0 ? 0 : Math.Max(TargetSize.MinBytes, (long)Math.Floor((double)Math.Max(0, totalTarget) * sizes[i] / sum));
        return result;
    }
}
