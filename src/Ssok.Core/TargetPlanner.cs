namespace Ssok.Core;

/// <summary>
/// "모두 합쳐서" 목표를 파일마다 나눈다. 원래 크기에 비례해서 나눈다.
/// 화면에서는 파일을 하나 끝낼 때마다 남은 예산으로 다시 나눠서,
/// 덜 줄어든 파일이 있으면 뒤 파일이 그만큼 더 줄도록 한다.
/// </summary>
public static class TargetPlanner
{
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
