namespace Ssok.Core;

/// <summary>
/// 결과 파일 저장. 원칙: 원본은 절대 덮어쓰지 않는다.
/// "사진.jpg" → "사진_쏙.jpg", 이미 있으면 "사진_쏙(2).jpg" ...
/// </summary>
public static class SafeOutput
{
    public const string Suffix = "_쏙";

    /// <param name="directory">저장할 폴더. 없으면 원본과 같은 폴더.</param>
    public static string PlanPath(string originalPath, string? newExtension = null, string? directory = null)
    {
        var dir = directory ?? Path.GetDirectoryName(Path.GetFullPath(originalPath))
                  ?? throw new ArgumentException("폴더를 알 수 없는 경로입니다.", nameof(originalPath));
        var name = Path.GetFileNameWithoutExtension(originalPath);
        var ext = newExtension ?? Path.GetExtension(originalPath);
        return Unique(dir, name + Suffix, ext);
    }

    /// <summary>dir 안에서 아직 없는 이름을 고른다: 이름.확장자 → 이름(2).확장자 …</summary>
    public static string Unique(string dir, string stem, string ext)
    {
        var candidate = Path.Combine(dir, stem + ext);
        for (int n = 2; File.Exists(candidate) || Directory.Exists(candidate); n++)
        {
            if (n > 9999) throw new IOException("같은 이름의 결과 파일이 너무 많습니다.");
            candidate = Path.Combine(dir, $"{stem}({n}){ext}");
        }
        return candidate;
    }

    /// <summary>
    /// 같은 폴더의 임시 파일에 먼저 쓰고 끝까지 써진 뒤에 이름을 바꾼다.
    /// 중간에 꺼져도 반쯤 쓰인 결과 파일이 남지 않고, 원본도 건드리지 않는다.
    /// </summary>
    /// <param name="directory">저장할 폴더. 없으면 원본과 같은 폴더.</param>
    public static string Write(string originalPath, byte[] data, string? newExtension = null, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var target = PlanPath(originalPath, newExtension, directory);
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("원본과 같은 경로에는 저장하지 않습니다.");
        WriteAtomic(target, fs => fs.Write(data));
        return target;
    }

    /// <summary>
    /// 이미 만든 결과 파일을 사용자가 고른 폴더로 복사한다. 같은 이름이 있으면 (2), (3)…을 붙이고
    /// 있던 파일은 덮어쓰지 않는다. 저장한 경로를 돌려준다.
    /// </summary>
    public static string CopyInto(string sourcePath, string destDirectory, string fileName)
    {
        var target = Unique(destDirectory, Path.GetFileNameWithoutExtension(fileName), Path.GetExtension(fileName));
        WriteAtomic(target, fs =>
        {
            using var src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            src.CopyTo(fs);
        });
        return target;
    }

    /// <summary>target 옆 임시 파일에 다 쓴 다음 이름을 바꾼다. 이미 있는 파일은 덮어쓰지 않는다.</summary>
    public static void WriteAtomic(string target, Action<FileStream> write, bool overwrite = false)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(target))!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".ssok-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(fs);
                fs.Flush(flushToDisk: true);
            }
            // overwrite:false — 그 사이 같은 이름이 생겼으면 덮어쓰지 않고 실패한다.
            File.Move(temp, target, overwrite);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* 다음 실행 때 정리 */ }
            }
        }
    }

    /// <summary>비정상 종료로 남은 임시 파일을 지운다. 쏙이 만든 이름 형식만 지운다.</summary>
    public static int CleanLeftovers(string directory)
    {
        if (!Directory.Exists(directory)) return 0;
        int removed = 0;
        foreach (var f in Directory.EnumerateFiles(directory, ".ssok-*.tmp"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddMinutes(-10)) { File.Delete(f); removed++; }
            }
            catch { /* 쓰는 중이거나 권한 없음: 건너뜀 */ }
        }
        return removed;
    }
}
