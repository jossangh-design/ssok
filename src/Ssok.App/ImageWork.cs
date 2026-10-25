using System.Diagnostics;
using System.IO;
using Ssok.Core;
using Ssok.Core.Documents;

namespace Ssok.App;

internal enum WorkResult { Done, Partial, AlreadySmall, Failed, Canceled }

/// <param name="Message">파일 이름 아래 한 줄 (예: "75dpi · 화질 45%", 실패 이유)</param>
internal sealed record WorkOutcome(WorkResult Result, long? ResultBytes, string? ResultPath, string Message)
{
    /// <summary>비교 화면용 원본·결과 그림 파일 (한글 문서 속 가장 큰 그림). 없으면 파일 자체로 비교한다.</summary>
    public string? PreviewBefore { get; init; }
    public string? PreviewAfter { get; init; }

    /// <summary>화질을 크게 낮추면(Force) 목표에 맞출 수도 있는 경우 true. 화면이 사용자에게 물어본다.</summary>
    public bool CanForce { get; init; }
}

/// <summary>진행 알림: (이번 시도 안에서 0~1, 설명 예: "가장 선명한 설정을 찾는 중 (시도 2)")</summary>
internal delegate void WorkProgress(double fraction, string note);

/// <summary>파일 하나를 줄이는 일. 원본은 읽기만 하고, 결과는 SafeOutput으로 새 파일에 쓴다.</summary>
internal static class ImageWork
{
    // 파일을 통째로 메모리에 올려 처리하므로 한도를 둔다.
    // 사진은 300MB, 문서(hwp·hwpx·PDF)는 1.5GB까지. 그보다 크면 .NET 배열 한도(약 2GB)에도 가깝다.
    private const long MaxPhotoBytes = 300L * 1024 * 1024;
    private const long MaxDocumentBytes = 1536L * 1024 * 1024;

    public static string QpdfPath => Path.Combine(AppContext.BaseDirectory, "qpdf", "qpdf.exe");
    public static string TempRoot => Path.Combine(Path.GetTempPath(), "Ssok");

    public static Task<WorkOutcome> ShrinkAsync(string path, FileKind kind, WorkOptions options, string? outputDir = null,
                                                CancellationToken cancel = default, WorkProgress? progress = null)
        => RunSta(() => Shrink(path, kind, options, outputDir, cancel, progress));

    /// <param name="outputDir">결과를 둘 폴더. 없으면 원본과 같은 폴더 (자가 시험용).</param>
    internal static WorkOutcome Shrink(string path, FileKind kind, WorkOptions options, string? outputDir = null,
                                       CancellationToken cancel = default, WorkProgress? progress = null)
    {
        var sw = Stopwatch.StartNew();
        string kindName = kind.ToString().ToLowerInvariant();
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return Fail("파일이 없어졌어요. 다시 넣어 주세요.");
            long before = info.Length;
            bool photo = FileKinds.IsPhoto(kind);
            long max = photo ? MaxPhotoBytes : MaxDocumentBytes;
            if (before > max) return Fail(photo ? "300MB보다 큰 사진은 처리하지 않아요." : "1.5GB보다 큰 문서는 처리하지 않아요.");
            // 문서는 원본·풀어 놓은 내용·새 문서를 함께 메모리에 두므로 원본의 약 3배가 필요하다.
            long need = before * (photo ? 2 : 3);
            long avail = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes - GC.GetTotalMemory(false);
            if (avail > 0 && need > avail)
                return Fail($"이 PC의 남은 메모리로는 처리하기 어려운 크기예요. 다른 프로그램을 닫고 다시 해 주세요. (필요: 약 {TargetSize.Format(need)})");

            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bytes = new byte[fs.Length];
                fs.ReadExactly(bytes);
            }

            if (outputDir is null) SafeOutput.CleanLeftovers(Path.GetDirectoryName(Path.GetFullPath(path))!);

            var outcome = FileKinds.IsPhoto(kind)
                ? ShrinkPicture(path, kind, bytes, options, outputDir, cancel, progress)
                : ShrinkDocument(path, kind, bytes, options, outputDir, cancel, progress);

            AppLog.Job(kindName, before, outcome.ResultBytes ?? 0, sw.Elapsed, outcome.Result.ToString().ToLowerInvariant());
            return outcome;
        }
        catch (OperationCanceledException)
        {
            AppLog.Job(kindName, 0, 0, sw.Elapsed, "canceled");
            return new WorkOutcome(WorkResult.Canceled, null, null, "멈췄어요. 원본은 그대로예요.");
        }
        catch (OutOfMemoryException ex)
        {
            AppLog.Error("shrink", ex);
            GC.Collect();
            return Fail("파일이 너무 커서 메모리가 모자라요. 다른 프로그램을 닫고 다시 해 주세요.");
        }
        catch (DocumentNotSupportedException ex)
        {
            AppLog.Job(kindName, 0, 0, sw.Elapsed, "not-supported");
            return Fail(ex.UserMessage);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("shrink", ex);
            return Fail("이 폴더에 저장할 권한이 없어요. 바탕화면 등 다른 폴더로 옮겨서 해 주세요.");
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070070))
        {
            AppLog.Error("shrink", ex);
            return Fail("디스크 공간이 부족해요.");
        }
        catch (IOException ex)
        {
            AppLog.Error("shrink", ex);
            return Fail("파일을 읽거나 쓰지 못했어요. 다른 프로그램에서 열려 있으면 닫고 다시 해 주세요.");
        }
        catch (Exception ex)
        {
            AppLog.Error("shrink", ex);
            return Fail("이 파일은 열 수 없어요. 깨졌거나 다른 형식일 수 있어요.");
        }
    }

    private static WorkOutcome Fail(string message) => new(WorkResult.Failed, null, null, message);

    /// <summary>사진을 어떤 형식으로 저장할지 정한다. HEIC·WebP·BMP는 "원래대로"여도 JPG로 저장한다.</summary>
    public static bool OutputsJpeg(FileKind kind, PhotoFormat format) => format switch
    {
        PhotoFormat.Jpg => true,
        PhotoFormat.Png => false,
        _ => kind != FileKind.Png,
    };

    private const string SearchNote = "가장 선명한 설정을 찾는 중";

    // '알아보기 어려워도 줄이기'를 골랐을 때의 사진 하한 (평소: 품질 50, 짧은 쪽 240px)
    private const int ForceMinQuality = 10;
    private const int ForceMinShortSide = 16;

    private static WorkOutcome ShrinkPicture(string path, FileKind kind, byte[] bytes, WorkOptions options, string? outputDir,
                                             CancellationToken cancel, WorkProgress? progress)
    {
        bool outJpeg = OutputsJpeg(kind, options.PhotoFormat);
        bool sameFormat = outJpeg ? kind == FileKind.Jpeg : kind == FileKind.Png;
        string? newExt = sameFormat ? null : (outJpeg ? ".jpg" : ".png");

        progress?.Invoke(0, "사진 여는 중");
        WpfImage image;
        try
        {
            image = WpfImage.Load(bytes, outJpeg, keepMetadata: !options.RemoveLocation);
        }
        catch (Exception ex) when (kind is FileKind.Heic or FileKind.Webp && ex is NotSupportedException or FileFormatException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Error("decode", ex);
            return Fail(kind == FileKind.Heic
                ? "이 PC에서 HEIC 사진을 열 수 없어요. Microsoft Store에서 'HEIF 이미지 확장'을 설치해 주세요."
                : "이 PC에서 WebP 그림을 열 수 없어요. Microsoft Store에서 'WebP 이미지 확장'을 설치해 주세요.");
        }

        // 형식을 바꾸거나 위치정보를 지워야 하면, 이미 작더라도 새로 저장한다.
        bool mustRewrite = !sameFormat || (options.RemoveLocation && image.HasLocation);
        long compareBytes = mustRewrite ? long.MaxValue : bytes.Length;
        // 같은 형식으로 위치정보만 지우는 경우에는 원본보다 커지지 않게 한다.
        long target = sameFormat && mustRewrite ? Math.Min(options.TargetBytes, bytes.LongLength) : options.TargetBytes;
        var search = new ShrinkOptions
        {
            TargetBytes = target,
            Cancel = cancel,
            // 사진은 보통 10번 안팎이면 끝난다.
            OnAttempt = a => progress?.Invoke(Math.Min(0.95, a / 10.0), $"{SearchNote} (시도 {a})"),
        };
        var result = SizeSearch.Find(image, compareBytes, search);
        // 평소 하한(품질 50·짧은 쪽 240px)으로 안 될 때만, 사용자가 고른 경우 하한을 크게 낮춰 다시 찾는다.
        // (처음부터 낮추면 평소 방식으로 맞출 수 있는 사진도 쓸데없이 화질이 떨어진다.)
        if (result.Status == ShrinkStatus.CannotReach && options.Force)
            result = SizeSearch.Find(image, compareBytes, search with { MinQuality = ForceMinQuality, MinShortSidePx = ForceMinShortSide });

        switch (result.Status)
        {
            case ShrinkStatus.AlreadySmall:
                return new WorkOutcome(WorkResult.AlreadySmall, bytes.Length, null, "이미 목표보다 작아요 · 그대로 저장");
            case ShrinkStatus.CannotReach:
                return options.Force
                    ? Fail("가장 작게 줄여도 목표에 못 미쳐요. 목표를 조금 올려 주세요.")
                    : Fail("글자를 알아볼 수 있는 선까지 줄여도 목표에 못 미쳐요. 목표를 조금 올려 주세요.") with { CanForce = true };
            default:
                var saved = SafeOutput.Write(path, result.Data!, newExt, outputDir);
                var parts = new List<string>();
                int longSide = (int)Math.Round(Math.Max(image.Width, image.Height) * result.Scale);
                int shortSide = (int)Math.Round(Math.Min(image.Width, image.Height) * result.Scale);
                parts.Add(result.Scale < 0.999 ? $"{longSide}px" : "원래 크기");
                if (image.SupportsQuality) parts.Add($"화질 {result.Quality}%");
                if (options.Force && ((image.SupportsQuality && result.Quality < 50) || (result.Scale < 0.999 && shortSide < 240)))
                    parts.Add("알아보기 어려울 수 있음");
                if (newExt is not null) parts.Add(outJpeg ? "JPG로" : "PNG로");
                if (options.RemoveLocation && image.HasLocation) parts.Add("위치정보 지움");
                return new WorkOutcome(WorkResult.Done, result.Data!.Length, saved, string.Join(" · ", parts));
        }
    }

    private static WorkOutcome ShrinkDocument(string path, FileKind kind, byte[] bytes, WorkOptions options, string? outputDir,
                                              CancellationToken cancel, WorkProgress? progress)
    {
        bool pdf = kind == FileKind.Pdf;
        bool gray = pdf && options.PdfGray;
        // 흑백으로 바꾸기를 켰으면 이미 작은 PDF도 바꿔서 저장한다.
        long targetBytes = gray ? Math.Min(options.TargetBytes, bytes.LongLength - 1) : options.TargetBytes;

        progress?.Invoke(0, "문서 여는 중");
        DocShrinkResult r;
        string? previewBefore = null, previewAfter = null;
        using (IDocumentPackage package = kind switch
        {
            FileKind.Hwpx => new HwpxPackage(bytes),
            FileKind.Hwp => new HwpPackage(bytes),
            FileKind.Pdf => new PdfPackage(bytes, QpdfPath, TempRoot, cancel),
            _ => throw new DocumentNotSupportedException("지원하지 않는 파일이에요."),
        })
        {
            r = DocumentShrinker.Shrink(package, new WicRecoder(forceGray: gray), targetBytes, cancel,
                (attempt, f) => progress?.Invoke(f, $"그림 줄이는 중 (시도 {attempt})"), options.Force);

            // 한글 문서는 가장 큰 그림으로 비교한다 (PDF는 1쪽을 그려서 비교).
            if (!pdf && outputDir is not null && r.Replacements is { Count: > 0 } map)
            {
                var biggest = package.Images.Where(im => map.ContainsKey(im.Key)).MaxBy(im => im.Data.LongLength);
                if (biggest is not null)
                {
                    var ext = biggest.Codec == ImageCodec.Png ? ".png" : ".jpg";
                    previewBefore = Path.Combine(outputDir, "preview-before" + ext);
                    previewAfter = Path.Combine(outputDir, "preview-after" + ext);
                    File.WriteAllBytes(previewBefore, biggest.Data);
                    File.WriteAllBytes(previewAfter, map[biggest.Key].Data);
                }
            }
        }

        if (r.Status == DocShrinkStatus.AlreadySmall)
            return new WorkOutcome(WorkResult.AlreadySmall, bytes.Length, null, "이미 목표보다 작아요 · 그대로 저장");

        // 그림만 줄여서는 목표에 못 미치는 PDF: 쪽을 그림으로 바꿔 목표에 맞춘다 (쏙 2.0 방식).
        if (pdf && options.PdfPagesToImages && r.Status != DocShrinkStatus.Done)
        {
            var page = PdfPageShrink.Run(path, targetBytes, gray,
                (attempt, f) => progress?.Invoke(f, $"{SearchNote} (시도 {attempt})"), cancel, options.Force);
            long imageRouteBytes = r.Data?.LongLength ?? long.MaxValue;
            if (page.Reached || page.Data.LongLength < imageRouteBytes)
            {
                if (page.Data.LongLength >= bytes.LongLength && !gray)
                    return Fail("이 PDF는 더 줄일 수 없어요. 이미 충분히 압축되어 있어요.") with { CanForce = !options.Force };
                var saved = SafeOutput.Write(path, page.Data, null, outputDir);
                // 사용자에게는 어려운 수치(dpi·화질) 대신 알아야 할 것만 보여 준다.
                var detail = "글자 검색·복사 안 됨" + (gray ? " · 흑백" : "");
                // 평소 단계표보다 낮은 단계(사용자가 고른 경우)면 알려 준다.
                if (page.Dpi < RasterSearch.Ladder[^1].Dpi) detail += " · 알아보기 어려울 수 있음";
                AppLog.Info($"pdf-pages dpi={page.Dpi} quality={page.Quality} reached={page.Reached}");
                return page.Reached
                    ? new WorkOutcome(WorkResult.Done, page.Data.Length, saved, detail)
                    : new WorkOutcome(WorkResult.Partial, page.Data.Length, saved, detail + FileItem.OverShareNote)
                        { CanForce = !options.Force };
            }
        }

        string what = $"그림 {r.ImagesChanged}개 · {r.MaxLongSide}px · 화질 {r.Quality}%" + (gray ? " · 흑백" : "")
                      + (r.Level >= DocumentShrinker.Ladder.Count ? " · 알아보기 어려울 수 있음" : "");
        switch (r.Status)
        {
            case DocShrinkStatus.NothingToShrink:
                return Fail(r.ImagesTotal == 0
                    ? "줄일 수 있는 그림이 없어요. 글자·표가 대부분인 문서예요."
                    : "그림을 줄여도 더 작아지지 않아요.");
            case DocShrinkStatus.BestEffort:
            {
                var saved = SafeOutput.Write(path, r.Data!, null, outputDir);
                return new WorkOutcome(WorkResult.Partial, r.Data!.Length, saved, what + FileItem.OverShareNote)
                    { PreviewBefore = previewBefore, PreviewAfter = previewAfter, CanForce = !options.Force };
            }
            default:
            {
                var saved = SafeOutput.Write(path, r.Data!, null, outputDir);
                return new WorkOutcome(WorkResult.Done, r.Data!.Length, saved, what)
                    { PreviewBefore = previewBefore, PreviewAfter = previewAfter };
            }
        }
    }

    /// <summary>오래된 임시 작업 폴더를 지운다 (비정상 종료 대비).</summary>
    public static void CleanTempRoot()
    {
        try
        {
            if (!Directory.Exists(TempRoot)) return;
            foreach (var d in Directory.EnumerateDirectories(TempRoot))
            {
                // hold-<프로세스번호>-…: 저장을 기다리는 결과. 그 쏙이 아직 켜져 있으면 두고, 꺼졌으면 지운다.
                var name = Path.GetFileName(d);
                bool stale;
                if (name.StartsWith("hold-", StringComparison.Ordinal))
                {
                    var parts = name.Split('-');
                    stale = parts.Length < 3 || !int.TryParse(parts[1], out var pid) || !IsSsokRunning(pid);
                }
                else
                {
                    stale = Directory.GetLastWriteTimeUtc(d) < DateTime.UtcNow.AddHours(-6);
                }
                if (stale)
                    try { Directory.Delete(d, recursive: true); } catch { }
            }
        }
        catch { }
    }

    private static bool IsSsokRunning(int pid)
    {
        if (pid == Environment.ProcessId) return true;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited && p.ProcessName.Equals("Ssok", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>윈도우 그림 기능은 STA 스레드에서 가장 안정적이라, 파일마다 전용 스레드를 쓴다.</summary>
    internal static Task<T> RunSta<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        {
            IsBackground = true,
            Name = "Ssok work",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
