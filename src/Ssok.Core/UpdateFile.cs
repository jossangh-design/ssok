using System.Security.Cryptography;

namespace Ssok.Core;

/// <summary>
/// 업데이트로 받은 내용을 안전하게 다루는 도우미. 네트워크 코드는 없고, Updater가 넘겨준 스트림·파일만 다룬다.
/// - 받는 양에 한도를 둔다 (가짜 서버가 끝없이 보내도 디스크·메모리가 차지 않게).
/// - 설치 파일은 다른 프로그램이 고치거나 지우지 못하게 연 채로 지문을 확인하고, 그 상태로 실행한다.
/// </summary>
public static class UpdateFile
{
    /// <summary>input을 output으로 옮긴다. maxBytes를 넘으면 멈추고 예외. 옮긴 양을 돌려준다.</summary>
    public static async Task<long> CopyLimitedAsync(Stream input, Stream output, long maxBytes,
                                                    IProgress<long>? progress = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancel)) > 0)
        {
            done += read;
            if (done > maxBytes) throw new InvalidDataException("받은 내용이 예상보다 큽니다.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancel);
            progress?.Report(done);
        }
        return done;
    }

    /// <summary>maxBytes까지만 읽어 바이트로 돌려준다. 넘으면 예외.</summary>
    public static async Task<byte[]> ReadLimitedAsync(Stream input, long maxBytes, CancellationToken cancel = default)
    {
        using var ms = new MemoryStream();
        await CopyLimitedAsync(input, ms, maxBytes, null, cancel);
        return ms.ToArray();
    }

    /// <summary>
    /// 파일을 읽기 전용·쓰기/지우기 금지로 열고 SHA-256 지문을 확인한다. 맞으면 연 채로 돌려준다
    /// (닫기 전까지 다른 프로그램이 바꿔치기할 수 없다). 맞지 않으면 파일을 지우고 예외.
    /// </summary>
    public static async Task<FileStream> OpenVerifiedAsync(string path, string expectedSha256, CancellationToken cancel = default)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, cancel));
            if (string.IsNullOrEmpty(expectedSha256) || !actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("내려받은 설치 파일의 지문이 맞지 않습니다.");
            fs.Position = 0;
            return fs;
        }
        catch
        {
            fs.Dispose();
            try { File.Delete(path); } catch { /* 다음 실행 때 임시 폴더 정리 */ }
            throw;
        }
    }
}
