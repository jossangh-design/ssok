using System.IO;
using System.Text.Json;
using Ssok.Core;

namespace Ssok.App;

/// <summary>마지막으로 쓴 설정을 기억한다. %AppData%\Ssok\settings.json (파일 이름·경로는 저장하지 않음)</summary>
internal sealed class UserSettings
{
    public bool Combined { get; set; }
    public double TargetMb { get; set; } = 5;
    public PhotoFormat PhotoFormat { get; set; } = PhotoFormat.Original;
    public bool RemoveLocation { get; set; } = true;
    public bool PdfGray { get; set; }
    public bool CheckUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }
    /// <summary>마지막 확인 때 찾은 새 버전 (있으면 다음 실행 때 바로 다시 보여 준다).</summary>
    public string? KnownNewVersion { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ssok", "settings.json");

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new UserSettings();
        }
        catch (Exception ex) { AppLog.Error("settings-load", ex); }
        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { AppLog.Error("settings-save", ex); }
    }
}
