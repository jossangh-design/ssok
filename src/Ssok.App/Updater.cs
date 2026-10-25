using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ssok.Core;

namespace Ssok.App;

/// <summary>
/// 새 버전 확인과 업데이트.
/// - GitHub 배포 페이지의 update.json과 그 서명(update.json.sig)만 받아 온다. 파일·이름·사용 기록은 보내지 않는다.
/// - update.json은 배포할 때 비밀 키로 서명하고, 쏙 안에 든 공개 키로 확인한다. 서명이 맞지 않으면 아무것도 하지 않는다.
/// - 설치 파일은 update.json에 적힌 지문(SHA-256)과 같을 때만 실행한다.
/// - 저장소 주소와 공개 키는 공식 배포(GitHub Actions)에서만 들어간다. 직접 빌드한 쏙은 업데이트를 확인하지 않는다.
/// </summary>
internal static class Updater
{
    public sealed record Release(Version Version, string SetupUrl, string Sha256, long Size, string Notes, string PageUrl);

    private static string? Meta(string key) => typeof(Updater).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } v ? v : null;

    /// <summary>예: "jossang/ssok"</summary>
    public static string? Repo => Meta("SsokUpdateRepo");
    private static string? PublicKey => Meta("SsokUpdatePublicKey");

    public static bool Enabled => Repo is not null && PublicKey is not null;

    public static Version Current => typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0);

    public static string ReleasesPage => $"https://github.com/{Repo}/releases/latest";

    /// <summary>설치판(%LocalAppData%\Programs\Ssok)으로 실행 중인지. 무설치판은 내려받기 페이지만 열어 준다.</summary>
    public static bool IsInstalled
    {
        get
        {
            var installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ssok");
            var here = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(here, installDir, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static HttpClient NewClient()
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Ssok/{Current.ToString(3)}");
        return http;
    }

    /// <summary>새 버전이 있으면 그 정보를, 없거나 확인할 수 없으면 null.</summary>
    public static async Task<Release?> CheckAsync(CancellationToken cancel = default)
    {
        if (!Enabled) return null;
        try
        {
            using var http = NewClient();
            var baseUrl = $"https://github.com/{Repo}/releases/latest/download/";
            // 받기 전부터 양을 제한한다 (가짜 서버가 끝없이 보내도 메모리가 차지 않게).
            var json = await GetLimitedAsync(http, baseUrl + "update.json", MaxJsonBytes, cancel);
            var sig = Encoding.UTF8.GetString(await GetLimitedAsync(http, baseUrl + "update.json.sig", MaxSigBytes, cancel)).Trim();
            if (!Verify(json, sig))
            {
                AppLog.Info("update-signature-invalid");
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var version = Version.Parse(root.GetProperty("version").GetString()!);
            var setup = root.GetProperty("setup").GetString()!;
            var sha = root.GetProperty("sha256").GetString()!;
            long size = root.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            var notes = root.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";
            // 설치 파일은 이 저장소의 배포 페이지에서만 받는다.
            if (!setup.StartsWith($"https://github.com/{Repo}/releases/download/", StringComparison.Ordinal)) return null;

            var page = $"https://github.com/{Repo}/releases/tag/v{version.ToString(3)}";
            return version > Current ? new Release(version, setup, sha, size, notes, page) : null;
        }
        catch (Exception ex)
        {
            // 인터넷이 안 되거나 학교 보안망에 막혀도 쏙 사용에는 지장이 없게 조용히 넘어간다.
            AppLog.Error("update-check", ex);
            return null;
        }
    }

    private const long MaxJsonBytes = 64 * 1024;
    private const long MaxSigBytes = 4 * 1024;
    private const long MaxSetupBytes = 300L * 1024 * 1024;

    private static async Task<byte[]> GetLimitedAsync(HttpClient http, string url, long maxBytes, CancellationToken cancel)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("받을 내용이 너무 큽니다.");
        await using var input = await response.Content.ReadAsStreamAsync(cancel);
        return await UpdateFile.ReadLimitedAsync(input, maxBytes, cancel);
    }

    private static bool Verify(byte[] data, string signatureBase64)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey!), out _);
            return ecdsa.VerifyData(data, Convert.FromBase64String(signatureBase64), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// 지문을 확인한 설치 파일. 닫기(Dispose) 전까지 파일이 잠겨 있어 다른 프로그램이 바꿔치기할 수 없다.
    /// </summary>
    public sealed class VerifiedSetup(string path, FileStream lockHandle) : IDisposable
    {
        public string Path => path;
        public void Dispose() => lockHandle.Dispose();
    }

    /// <summary>설치 파일을 내려받아 지문을 확인한 뒤 잠근 채로 돌려준다. 지문이 다르거나 너무 크면 지우고 예외.</summary>
    public static async Task<VerifiedSetup> DownloadAsync(Release release, IProgress<double>? progress, CancellationToken cancel = default)
    {
        if (release.Size > MaxSetupBytes) throw new InvalidDataException("설치 파일이 너무 큽니다.");
        // 받을 때마다 새 폴더를 써서, 미리 같은 이름의 파일을 만들어 두는 일을 막는다.
        var dir = Path.Combine(Path.GetTempPath(), "Ssok", "update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"Ssok-Setup-{release.Version.ToString(3)}.exe");

        try
        {
            using var http = NewClient();
            http.Timeout = TimeSpan.FromMinutes(10);
            using (var response = await http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxSetupBytes) throw new InvalidDataException("설치 파일이 너무 큽니다.");
                long total = response.Content.Headers.ContentLength ?? release.Size;
                await using var input = await response.Content.ReadAsStreamAsync(cancel);
                await using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var report = progress is null || total <= 0 ? null : new Progress<long>(done => progress!.Report((double)done / total));
                await UpdateFile.CopyLimitedAsync(input, output, MaxSetupBytes, report, cancel);
            }
        }
        catch
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
            throw;
        }

        // 지문 확인부터 실행까지 같은 잠금을 유지한다.
        var locked = await UpdateFile.OpenVerifiedAsync(file, release.Sha256, cancel);
        return new VerifiedSetup(file, locked);
    }

    /// <summary>설치 파일을 조용히 실행한다. 설치가 끝나면 설치 파일이 쏙을 다시 켠다.</summary>
    public static void RunInstaller(VerifiedSetup setup)
    {
        Process.Start(new ProcessStartInfo(setup.Path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS")
        {
            UseShellExecute = true,
        });
    }

    public static void OpenPage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Error("open-page", ex); }
    }
}
