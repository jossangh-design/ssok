using System.Globalization;

namespace Ssok.Core;

/// <summary>
/// 오류 확인용 기록. 원칙: 파일 이름·폴더 경로·문서 내용은 절대 남기지 않는다.
/// 남기는 것: 시각, 파일 종류(확장자), 크기, 걸린 시간, 결과, 오류 종류 이름.
/// 위치: %AppData%\Ssok\logs\yyyy-MM-dd.log, 14일이 지난 기록은 지운다.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private const int KeepDays = 14;

    public static string Directory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ssok", "logs");

    public static void Job(string kind, long beforeBytes, long afterBytes, TimeSpan elapsed, string outcome)
        => Write($"job kind={Safe(kind)} before={beforeBytes} after={afterBytes} ms={(long)elapsed.TotalMilliseconds} outcome={Safe(outcome)}");

    /// <summary>예외 메시지에는 경로가 들어가는 경우가 많아서 종류 이름만 남긴다.</summary>
    public static void Error(string where, Exception ex)
        => Write($"error at={Safe(where)} type={ex.GetType().Name} hresult=0x{ex.HResult:X8}");

    public static void Info(string message) => Write("info " + Safe(message));

    private static string Safe(string s)
    {
        // 경로처럼 보이는 글자가 섞여 들어오면 지운다 (실수 방지용 안전장치).
        if (s.Contains('\\') || s.Contains('/') || s.Contains(':')) return "[생략]";
        return s.Replace('\n', ' ').Replace('\r', ' ');
    }

    private static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var now = DateTime.Now;
                var file = Path.Combine(Directory, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
                File.AppendAllText(file, now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + line + Environment.NewLine);
            }
        }
        catch
        {
            // 기록 실패가 작업을 막으면 안 된다.
        }
    }

    public static void Prune()
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return;
            foreach (var f in System.IO.Directory.EnumerateFiles(Directory, "*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-KeepDays)) File.Delete(f);
        }
        catch { }
    }
}
