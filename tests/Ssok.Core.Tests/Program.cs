using System.IO.Compression;
using Ssok.Core;
using Ssok.Core.Documents;

int failed = 0, passed = 0;

void Check(string name, bool ok, string detail = "")
{
    if (ok) { passed++; Console.WriteLine($"  통과  {name}"); }
    else { failed++; Console.WriteLine($"  실패  {name} {detail}"); }
}

Console.WriteLine("[목표 용량 읽기]");
Check("300KB", TargetSize.TryParse("300KB", out var b1) && b1 == 300 * 1024);
Check("1.5 MB", TargetSize.TryParse("1.5 MB", out var b2) && b2 == (long)(1.5 * 1024 * 1024));
Check("단위 없으면 KB", TargetSize.TryParse("800", out var b3) && b3 == 800 * 1024);
Check("쉼표 소수점", TargetSize.TryParse("2,5mb", out var b4) && b4 == (long)(2.5 * 1024 * 1024));
Check("너무 작으면 거부", !TargetSize.TryParse("5KB", out _));
Check("글자는 거부", !TargetSize.TryParse("많이", out _));
Check("빈칸 거부", !TargetSize.TryParse("  ", out _));
Check("표시 1MB", TargetSize.Format(1024 * 1024) == "1MB", TargetSize.Format(1024 * 1024));
Check("표시 300KB", TargetSize.Format(300 * 1024) == "300KB", TargetSize.Format(300 * 1024));

Console.WriteLine("[목표 용량 찾기]");
{
    var img = new FakeImage(4000, 3000, lossy: true, bytesPerPixelAt100: 0.3); // 92품질 ≈ 3.3MB, 50품질 ≈ 1.8MB
    var r = SizeSearch.Find(img, 4_000_000, new ShrinkOptions { TargetBytes = 2 * 1024 * 1024 });
    Check("품질만 낮춰서 도달", r.Status == ShrinkStatus.Done && r.Scale == 1.0, $"{r}");
    Check("결과가 목표 이하", r.Data!.Length <= 2 * 1024 * 1024);
    Check("가능한 가장 높은 품질", img.Encode(1.0, r.Quality + 1).Length > 2 * 1024 * 1024, $"q={r.Quality}");
    Check("시도 횟수 적당(≤ 10)", r.Attempts <= 10, $"attempts={r.Attempts}");
}
{
    var img = new FakeImage(4000, 3000, lossy: true, bytesPerPixelAt100: 0.5);
    var r = SizeSearch.Find(img, 6_000_000, new ShrinkOptions { TargetBytes = 500 * 1024 });
    Check("최소 품질로 안 되면 크기 줄임", r.Status == ShrinkStatus.Done && r.Scale < 1.0 && r.Quality >= 50, $"{r.Status} s={r.Scale:0.###} q={r.Quality}");
    Check("품질은 최소 50 이상", img.Log.All(x => x.Quality >= 50));
    Check("결과가 목표 이하", r.Data!.Length <= 500 * 1024);
    Check("크기 줄일 때도 시도 횟수 적당(≤ 16)", r.Attempts <= 16, $"attempts={r.Attempts}");
}
{
    // '멈추기': 취소하면 더 시도하지 않고 바로 멈춘다.
    var img = new FakeImage(4000, 3000, lossy: true, bytesPerPixelAt100: 0.5);
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    bool stopped = false;
    try { SizeSearch.Find(img, 6_000_000, new ShrinkOptions { TargetBytes = 500 * 1024, Cancel = cts.Token }); }
    catch (OperationCanceledException) { stopped = true; }
    Check("멈추기 누르면 바로 멈춤", stopped && img.Calls == 0, $"calls={img.Calls}");
}
{
    var img = new FakeImage(1000, 800, lossy: true, bytesPerPixelAt100: 0.5);
    var r = SizeSearch.Find(img, 100 * 1024, new ShrinkOptions { TargetBytes = 200 * 1024 });
    Check("이미 작으면 손대지 않음", r.Status == ShrinkStatus.AlreadySmall && r.Data is null && img.Calls == 0);
}
{
    var img = new FakeImage(4000, 3000, lossy: true, bytesPerPixelAt100: 0.5);
    var r = SizeSearch.Find(img, 6_000_000, new ShrinkOptions { TargetBytes = 10 * 1024 });
    Check("불가능하면 CannotReach", r.Status == ShrinkStatus.CannotReach && r.Data is null, $"{r.Status}");
    Check("짧은 쪽 240px 아래로는 안 줄임", img.Log.All(x => Math.Min(4000, 3000) * x.Scale >= 240 - 0.001));
}
{
    var img = new FakeImage(3000, 2000, lossy: false, bytesPerPixelAt100: 1.0);
    var r = SizeSearch.Find(img, 6_000_000, new ShrinkOptions { TargetBytes = 1024 * 1024 });
    Check("PNG는 크기만 줄여 도달", r.Status == ShrinkStatus.Done && r.Scale < 1.0 && r.Data!.Length <= 1024 * 1024, $"{r.Status} s={r.Scale:0.###}");
}
{
    var img = new FakeImage(300, 200, lossy: false, bytesPerPixelAt100: 4.0);
    var r = SizeSearch.Find(img, 240_000, new ShrinkOptions { TargetBytes = 20 * 1024 });
    Check("작은 그림은 절반까지만 줄임", img.Log.All(x => x.Scale >= 0.5 - 0.001), $"{r.Status}");
}

{
    // 목표를 키우면 결과도 커져야 한다 (목표를 넉넉히 주면 화질을 최대한 살림)
    long Size(long target) => SizeSearch.Find(new FakeImage(4000, 3000, true, 0.3), 4_000_000, new ShrinkOptions { TargetBytes = target }).Data!.Length;
    long s3 = Size(3 * 1024 * 1024), s2 = Size(2 * 1024 * 1024), s1 = Size(1024 * 1024);
    Check("목표가 클수록 결과도 큼", s3 > s2 && s2 > s1, $"{s1},{s2},{s3}");
    Check("넉넉하면 목표에 가깝게", s3 > 3 * 1024 * 1024 * 0.9, $"{s3}");
}
{
    // 품질 상한 95: 95보다 높은 품질로는 저장하지 않는다. 목표가 넉넉하면 95로 저장한다.
    var img = new FakeImage(4000, 3000, lossy: true, bytesPerPixelAt100: 0.3);
    var r = SizeSearch.Find(img, 50_000_000, new ShrinkOptions { TargetBytes = 40_000_000 });
    Check("품질은 95까지만", img.Log.All(x => x.Quality <= 95) && r.Quality == 95, $"q={r.Quality}");
    var tight = new FakeImage(4000, 3000, lossy: true, bytesPerPixelAt100: 0.3);
    SizeSearch.Find(tight, 4_000_000, new ShrinkOptions { TargetBytes = 2 * 1024 * 1024 });
    Check("품질을 찾을 때도 95를 넘지 않음", tight.Log.All(x => x.Quality <= 95));
}

Console.WriteLine("[결과 파일 저장]");
{
    var dir = Path.Combine(Path.GetTempPath(), "ssok-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var original = Path.Combine(dir, "학급 사진.jpg");
        File.WriteAllBytes(original, new byte[] { 1, 2, 3 });

        var p1 = SafeOutput.Write(original, new byte[] { 9 });
        Check("이름에 _쏙", Path.GetFileName(p1) == "학급 사진_쏙.jpg", Path.GetFileName(p1));
        var p2 = SafeOutput.Write(original, new byte[] { 8 });
        Check("겹치면 (2)", Path.GetFileName(p2) == "학급 사진_쏙(2).jpg", Path.GetFileName(p2));
        Check("원본 그대로", File.ReadAllBytes(original).SequenceEqual(new byte[] { 1, 2, 3 }));
        Check("임시 파일 안 남음", !Directory.EnumerateFiles(dir, ".ssok-*.tmp").Any());
        var p3 = SafeOutput.Write(original, new byte[] { 7 }, ".png");
        Check("확장자 바꾸기", Path.GetFileName(p3) == "학급 사진_쏙.png", Path.GetFileName(p3));

        // 잠시 보관한 결과 → 사용자가 고른 폴더로 저장
        var hold = Path.Combine(dir, "hold");
        Directory.CreateDirectory(hold);
        var held = SafeOutput.Write(original, new byte[] { 5, 5 }, null, hold);
        Check("보관 폴더에 씀", Path.GetDirectoryName(held) == hold && Path.GetFileName(held) == "학급 사진_쏙.jpg", held);
        var dest = Path.Combine(dir, "저장");
        Directory.CreateDirectory(dest);
        var s1 = SafeOutput.CopyInto(held, dest, "학급 사진_쏙.jpg");
        var s2 = SafeOutput.CopyInto(held, dest, "학급 사진_쏙.jpg");
        Check("고른 폴더에 저장", File.ReadAllBytes(s1).SequenceEqual(new byte[] { 5, 5 }));
        Check("같은 이름이면 덮어쓰지 않고 (2)", Path.GetFileName(s2) == "학급 사진_쏙(2).jpg", Path.GetFileName(s2));
        Check("저장 뒤 임시 파일 안 남음", !Directory.EnumerateFiles(dest, ".ssok-*.tmp").Any());
    }
    finally { Directory.Delete(dir, recursive: true); }
}

Console.WriteLine("[쪽을 그림으로 바꾼 PDF]");
{
    // 작은 JPEG 두 장으로 두 쪽짜리 PDF를 만들어 본다.
    byte[] jpg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAYEBQYFBAYGBQYHBwYIChAKCgkJChQODwwQFxQYGBcUFhYaHSUfGhsjHBYWICwgIyYnKSopGR8tMC0oMCUoKSj/2wBDAQcHBwoIChMKChMoGhYaKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCj/wAARCAAIAAgDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDiqKKK8A/Wz//Z");
    var pdf = PdfImageWriter.Write(new[]
    {
        new PageImage(jpg, 8, 8, 595.28, 841.89, false),
        new PageImage(jpg, 8, 8, 841.89, 595.28, false),
    });
    var text = System.Text.Encoding.Latin1.GetString(pdf);
    Check("PDF 머리말", text.StartsWith("%PDF-1.4"));
    Check("쪽 2개", text.Contains("/Count 2"));
    Check("가로 쪽 크기", text.Contains("/MediaBox [0 0 841.89 595.28]"));
    Check("컬러 쪽은 DeviceRGB", text.Contains("/DeviceRGB"));
    // xref에 적힌 위치가 실제 객체 위치와 맞는지
    int xrefAt = text.LastIndexOf("startxref\n", StringComparison.Ordinal);
    long xrefPos = long.Parse(text[(xrefAt + 10)..].Split('\n')[0]);
    var lines = text[(int)xrefPos..].Split('\n');
    bool offsetsOk = true;
    for (int n = 1; n <= 8; n++)
    {
        long off = long.Parse(lines[2 + n][..10]);
        offsetsOk &= text.AsSpan((int)off).StartsWith($"{n} 0 obj");
    }
    Check("목차(xref) 위치 정확", offsetsOk);
    var outFile = Environment.GetEnvironmentVariable("SSOK_RASTER_PDF_OUT");
    if (outFile is not null) File.WriteAllBytes(outFile, pdf);
}
{
    // 단계가 뒤로 갈수록 작아지는 가짜 문서: 목표 안에 드는 가장 선명한 단계를 고른다.
    long SizeAt(int level) => 100_000_000L / (level + 1);
    var built = new List<int>();
    var r = RasterSearch.Find(l => { built.Add(l); return new byte[SizeAt(l) / 10_000]; }, 2_000);
    Check("목표 안의 가장 선명한 단계", r.Reached && r.Level == 4 && r.Data.Length <= 2_000, $"level={r.Level}");
    Check("단계마다 한 번만 만듦", built.Distinct().Count() == built.Count && r.Attempts == built.Count && built.Count <= 5,
          string.Join(",", built));
    // 어림값이 정확하면 한두 번 만에 끝난다.
    var built2 = new List<int>();
    var e = RasterSearch.FindWithEstimate(l => { built2.Add(l); return new byte[SizeAt(l) / 10_000]; }, l => SizeAt(l) / 10_000, 2_000);
    Check("어림값으로 바로 찾기", e.Reached && e.Level == 4 && built2.Count <= 2, string.Join(",", built2));
    // 어림값이 실제보다 작게 나와도 (빗나가도) 목표 안으로 맞춘다.
    var e2 = RasterSearch.FindWithEstimate(l => new byte[SizeAt(l) / 10_000], l => SizeAt(l) / 20_000, 2_000);
    Check("어림이 빗나가도 목표 안", e2.Reached && e2.Data.Length <= 2_000 && e2.Level == 4, $"level={e2.Level}");
    var miss = RasterSearch.Find(l => new byte[1000], 10);
    Check("못 미치면 가장 작은 단계로 목표 미달", !miss.Reached && miss.Level == RasterSearch.Ladder.Count - 1);
}

Console.WriteLine("[기록에 경로 남기지 않기]");
{
    var dir = Path.Combine(Path.GetTempPath(), "ssok-log-" + Guid.NewGuid().ToString("N"));
    AppLog.Directory = dir;
    AppLog.Info(@"C:\Users\선생님\학생명단.hwp 처리");
    AppLog.Error("save", new IOException(@"C:\Users\선생님\비밀.pdf 를 찾을 수 없음"));
    AppLog.Job("jpg", 5_000_000, 900_000, TimeSpan.FromMilliseconds(420), "done");
    var text = string.Join("\n", Directory.EnumerateFiles(dir).Select(File.ReadAllText));
    Check("경로·파일명 없음", !text.Contains("학생명단") && !text.Contains("비밀") && !text.Contains(@"C:\"), text);
    Check("작업 기록은 남음", text.Contains("before=5000000"));
    Directory.Delete(dir, recursive: true);
}

Console.WriteLine("[모두 합쳐서 나누기]");
{
    var t = TargetPlanner.Split(10_000_000, new long[] { 8_000_000, 8_000_000, 4_000_000 });
    Check("합계가 목표 이하", t.Sum() <= 10_000_000, string.Join(",", t));
    Check("크기에 비례", t[0] == t[1] && t[0] > t[2], string.Join(",", t));
    var t2 = TargetPlanner.Split(100_000, new long[] { 30_000_000, 30_000_000 });
    Check("너무 작은 몫은 최소 10KB", t2.All(x => x >= TargetSize.MinBytes), string.Join(",", t2));
    var t3 = TargetPlanner.Split(10_000_000, new long[] { 2_000_000, 3_000_000 });
    Check("모두 작으면 그대로", t3[0] == 2_000_000 && t3[1] == 3_000_000);
}
Console.WriteLine("[목표 여유]");
{
    long safe10 = TargetSize.SafeBytesFromMb(10);
    Check("10MB는 9,800,000바이트로 맞춤", safe10 == 9_800_000, $"{safe10}");
    Check("어느 방식으로 세도 10MB 이하", safe10 < 10_000_000 && safe10 < 10L * 1024 * 1024);
    Check("0.5MB(500KB)는 490,000바이트", TargetSize.SafeBytesFromMb(0.5) == 490_000, $"{TargetSize.SafeBytesFromMb(0.5)}");

    // 실제로 ZIP(압축 안 함)을 만들어, 어림한 양이 진짜로 늘어난 양 이상인지 확인한다.
    var names = new[] { "학급 사진_쏙.jpg", "가정통신문(3월) 최종_쏙.hwp", "report_쏙.pdf", "가정통신문(3월) 최종_쏙(2).hwp" };
    var sizes = new[] { 300_000, 1_200_000, 55_555, 7 };
    using var zms = new MemoryStream();
    using (var zip = new ZipArchive(zms, ZipArchiveMode.Create, leaveOpen: true))
    {
        for (int i = 0; i < names.Length; i++)
        {
            var data = new byte[sizes[i]];
            Random.Shared.NextBytes(data);
            using var s = zip.CreateEntry(names[i], CompressionLevel.NoCompression).Open();
            s.Write(data);
        }
    }
    long grown = zms.Length - sizes.Sum();
    long planned = TargetPlanner.ZipOverhead(names);
    Check("ZIP으로 늘어나는 양을 넉넉히 어림", grown > 0 && grown <= planned, $"실제 {grown} · 어림 {planned}");
    Check("어림이 지나치게 크지 않음 (1KB 이내)", planned - grown < 1024, $"실제 {grown} · 어림 {planned}");
}

Console.WriteLine("[파일 종류]");
Check("heic는 사진", FileKinds.IsPhoto(FileKinds.Detect("a.HEIC")) && FileKinds.Detect("b.webp") == FileKind.Webp);
Check("hwpx는 한글", FileKinds.Group(FileKinds.Detect("c.hwpx")) == FileGroup.Hangul);

Console.WriteLine("[문서: 단계 찾기]");
{
    var pkg = new FakePackage(100_000, 6, 150_000); // 원본 1,000,000
    var r = DocumentShrinker.Shrink(pkg, new FakeRecoder(), 700_000);
    Check("목표 이하로 맞춤", r.Status == DocShrinkStatus.Done && r.Data!.Length <= 700_000, $"{r.Status} {r.Data?.Length}");
    Check("가장 약한 단계 선택", r.Level == 0 || pkg.SizeAt(r.Level - 1) > 700_000, $"level={r.Level}");
    Check("만들기 횟수 적음(≤ 5)", r.Builds <= 5, $"builds={r.Builds}");
    var small = DocumentShrinker.Shrink(new FakePackage(100_000, 3, 100_000), new FakeRecoder(), 600_000);
    Check("이미 작으면 그대로", small.Status == DocShrinkStatus.AlreadySmall && small.Data is null);
    var none = DocumentShrinker.Shrink(new FakePackage(900_000, 0, 0), new FakeRecoder(), 100_000);
    Check("그림 없으면 NothingToShrink", none.Status == DocShrinkStatus.NothingToShrink);
    var hard = DocumentShrinker.Shrink(new FakePackage(900_000, 2, 50_000), new FakeRecoder(), 300_000);
    Check("글자가 대부분이면 BestEffort", hard.Status == DocShrinkStatus.BestEffort && hard.Data!.Length < 1_000_000 && hard.Data!.Length > 300_000, $"{hard.Status}");
}

Console.WriteLine("[문서: 큰 그림부터 줄이기]");
{
    Check("몫이 작을수록 약하게 줄임", DocumentShrinker.LighterBy(1, 100) == 3 && DocumentShrinker.LighterBy(4, 100) == 2
          && DocumentShrinker.LighterBy(10, 100) == 1 && DocumentShrinker.LighterBy(50, 100) == 0);
    // 큰 사진 하나 + 작은 도표 셋: 같은 결과 안에서 작은 그림은 덜 줄인다.
    var pkg = new SizedPackage(20_000, new[] { 900_000, 10_000, 10_000, 10_000 });
    var r = DocumentShrinker.Shrink(pkg, new FakeRecoder(), 400_000);
    double Ratio(string key) => (double)r.Replacements![key].Data.Length / pkg.Images.First(i => i.Key == key).Data.Length;
    Check("큰 그림 목표 맞춤", r.Status == DocShrinkStatus.Done && r.Data!.Length <= 400_000, $"{r.Status} {r.Data?.Length}");
    Check("작은 그림은 덜 줄임", r.Replacements is not null && Ratio("i1") > Ratio("i0"),
          r.Replacements is null ? "" : $"{Ratio("i1"):0.###} vs {Ratio("i0"):0.###}");
    // 가장 센 단계에서는 모두 똑같이 줄이므로, 맞출 수 있는 가장 작은 크기는 그대로다.
    var tight = DocumentShrinker.Shrink(pkg, new FakeRecoder(), 1);
    var uniform = DocumentShrinker.Ladder[^1];
    long expected = 20_000 + pkg.Images.Sum(i => (long)new FakeRecoder().Recode(i, uniform.MaxLongSide, uniform.Quality)!.Data.Length);
    Check("가장 작게 줄인 크기는 그대로", tight.Status == DocShrinkStatus.BestEffort && tight.Data!.Length == expected,
          $"{tight.Data?.Length} vs {expected}");
}

Console.WriteLine("[알아보기 어려워도 줄이기]");
{
    var pkg = new FakePackage(10_000, 4, 1_000_000);
    var normal = DocumentShrinker.Shrink(pkg, new FakeRecoder(), 60_000);
    Check("문서: 평소 단계로는 목표 미달", normal.Status == DocShrinkStatus.BestEffort, $"{normal.Status}");
    var forced = DocumentShrinker.Shrink(pkg, new FakeRecoder(), 60_000, force: true);
    Check("문서: 고르면 목표에 맞춤", forced.Status == DocShrinkStatus.Done && forced.Data!.Length <= 60_000 && forced.MaxLongSide < 320,
          $"{forced.Status} {forced.Data?.Length} {forced.MaxLongSide}px");

    long SizeAt(int level) => 100_000_000L / (level + 1);
    var page = RasterSearch.Find(l => new byte[SizeAt(l) / 10_000], 600);
    Check("PDF 쪽 그림: 평소 단계로는 목표 미달", !page.Reached);
    var pageForced = RasterSearch.Find(l => new byte[SizeAt(l) / 10_000], 600, null, default, RasterSearch.LadderFor(true));
    Check("PDF 쪽 그림: 고르면 목표에 맞춤", pageForced.Reached && pageForced.Level >= RasterSearch.Ladder.Count, $"level={pageForced.Level}");

    var img = new FakeImage(4000, 3000, lossy: true, bytesPerPixelAt100: 0.5);
    var photo = SizeSearch.Find(img, 6_000_000, new ShrinkOptions { TargetBytes = 10 * 1024, MinQuality = 10, MinShortSidePx = 16 });
    Check("사진: 고르면 목표에 맞춤", photo.Status == ShrinkStatus.Done && photo.Data!.Length <= 10 * 1024, $"{photo.Status}");
}

Console.WriteLine("[PDF 시간 제한]");
Check("작은 PDF는 3분", PdfPackage.TimeoutFor(1024 * 1024) == TimeSpan.FromMinutes(3));
Check("200MB PDF는 13분", PdfPackage.TimeoutFor(200L * 1024 * 1024) == TimeSpan.FromMinutes(13));
Check("아주 커도 30분까지", PdfPackage.TimeoutFor(3L * 1024 * 1024 * 1024) == TimeSpan.FromMinutes(30));

var fixtures = TestUtil.FindFixtures();
Console.WriteLine("[문서: hwpx]");
{
    var original = File.ReadAllBytes(Path.Combine(fixtures, "sample.hwpx"));
    using var pkg = new HwpxPackage(original);
    Check("그림 2개 찾음", pkg.Images.Count == 2, $"{pkg.Images.Count}");
    var r = DocumentShrinker.Shrink(pkg, new FakeRecoder(), (long)(original.Length * 0.8));
    Check("줄어듦", r.Status == DocShrinkStatus.Done && r.Data!.Length <= original.Length * 0.8, $"{r.Status} {r.Data?.Length}/{original.Length}");
    using var a = new ZipArchive(new MemoryStream(original));
    using var b = new ZipArchive(new MemoryStream(r.Data!));
    Check("항목 순서·이름 같음", a.Entries.Select(e => e.FullName).SequenceEqual(b.Entries.Select(e => e.FullName)));
    Check("mimetype 맨 앞·무압축", b.Entries[0].FullName == "mimetype" && b.Entries[0].CompressedLength == b.Entries[0].Length);
    string Text(ZipArchive z, string n) { using var sr = new StreamReader(z.GetEntry(n)!.Open()); return sr.ReadToEnd(); }
    Check("본문 XML 그대로", a.Entries.Where(e => e.FullName.EndsWith(".xml")).All(e => Text(a, e.FullName) == Text(b, e.FullName)));
    using var again = new HwpxPackage(r.Data!);
    Check("다시 열림", again.Images.Count == 2);
}
{
    using var ms = new MemoryStream();
    using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
    {
        z.CreateEntry("mimetype").Open().Dispose();
        z.CreateEntry("../evil.txt").Open().Dispose();
    }
    Check("이상한 경로 거부", TestUtil.Throws<DocumentNotSupportedException>(() => new HwpxPackage(ms.ToArray())));
}
{
    // 전자서명·암호 hwpx는 바꾸지 않는다 (서명이 깨지거나 문서가 안 열리므로).
    var sample = File.ReadAllBytes(Path.Combine(fixtures, "sample.hwpx"));
    var utf8 = System.Text.Encoding.UTF8;
    var signed = TestUtil.ZipWith(sample, "META-INF/signatures.xml", utf8.GetBytes("<signatures/>"));
    Check("전자서명 hwpx 거부", TestUtil.Throws<DocumentNotSupportedException>(() => new HwpxPackage(signed)));
    var encrypted = TestUtil.ZipWith(sample, "META-INF/manifest.xml", utf8.GetBytes(
        "<odf:manifest xmlns:odf=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\"><odf:file-entry odf:full-path=\"Contents/section0.xml\">" +
        "<odf:encryption-data odf:checksum-type=\"SHA1\"/></odf:file-entry></odf:manifest>"));
    Check("암호 hwpx 거부 (manifest 표시)", TestUtil.Throws<DocumentNotSupportedException>(() => new HwpxPackage(encrypted)));
    var scrambled = TestUtil.ZipWith(sample, "Contents/section0.xml", new byte[] { 0x9C, 0x3A, 0x11, 0xF0, 0x42 });
    Check("암호 hwpx 거부 (본문이 XML 아님)", TestUtil.Throws<DocumentNotSupportedException>(() => new HwpxPackage(scrambled)));
    var withBom = TestUtil.ZipWith(sample, "Contents/section0.xml", new byte[] { 0xEF, 0xBB, 0xBF, (byte)'\n', (byte)'<', (byte)'a', (byte)'/', (byte)'>' });
    Check("BOM 있는 본문 XML은 그대로 열림", !TestUtil.Throws<Exception>(() => new HwpxPackage(withBom).Dispose()));
}

Console.WriteLine("[문서: 한글 그림 형식 바꾸기]");
{
    // PNG(투명 없음)·BMP는 JPEG으로 바꾸고, 문서 목록·DocInfo의 이름도 함께 고친다.
    var hwpx = File.ReadAllBytes(Path.Combine(fixtures, "sample.hwpx"));
    using var px = new HwpxPackage(hwpx);
    var png = px.Images.FirstOrDefault(i => i.Codec == ImageCodec.Png);
    Check("hwpx PNG는 JPEG으로 바꿀 수 있음", png is not null && png.MayBecomeJpeg);
    if (png is not null)
    {
        var built = px.Build(new Dictionary<string, RecodedImage> { [png.Key] = JpegRecoder.Make() });
        using var z = new ZipArchive(new MemoryStream(built));
        var jpgName = png.Key[..png.Key.LastIndexOf('.')] + ".jpg";
        string hpf; using (var sr = new StreamReader(z.Entries.First(e => e.FullName.EndsWith("content.hpf")).Open())) hpf = sr.ReadToEnd();
        Check("hwpx 그림 이름 바뀜", z.GetEntry(jpgName) is not null && z.GetEntry(png.Key) is null, jpgName);
        Check("hwpx 문서 목록 고침", hpf.Contains("href=\"" + jpgName + "\"") && hpf.Contains("image/jpg") && !hpf.Contains("href=\"" + png.Key + "\""));
        Check("hwpx mimetype 맨 앞", z.Entries[0].FullName == "mimetype");
        using var again = new HwpxPackage(built);
        Check("hwpx 다시 열림·그림 수 같음", again.Images.Count == px.Images.Count && again.Images.Any(i => i.Key == jpgName && i.Codec == ImageCodec.Jpeg));
    }

    var hwp = File.ReadAllBytes(Path.Combine(fixtures, "sample.hwp"));
    using var ph = new HwpPackage(hwp);
    var hpng = ph.Images.FirstOrDefault(i => i.Codec == ImageCodec.Png);
    Check("hwp PNG는 JPEG으로 바꿀 수 있음 (DocInfo 확장자 찾음)", hpng is not null && hpng.MayBecomeJpeg);
    if (hpng is not null)
    {
        var built = ph.Build(new Dictionary<string, RecodedImage> { [hpng.Key] = JpegRecoder.Make() });
        using var again = new HwpPackage(built);
        var jpgName = hpng.Key[..hpng.Key.LastIndexOf('.')] + ".jpg";
        var renamed = again.Images.FirstOrDefault(i => i.Key.Equals(jpgName, StringComparison.OrdinalIgnoreCase));
        Check("hwp 스트림 이름 바뀜", renamed is not null && renamed.Codec == ImageCodec.Jpeg, string.Join(",", again.Images.Select(i => i.Key)));
        Check("hwp 그림 수 같음", again.Images.Count == ph.Images.Count);
        // DocInfo의 확장자도 jpg로 바뀌었으면, 다시 연 문서에서 그 이름으로 형식을 또 바꿀 수 있다.
        if (renamed is not null)
        {
            var pngHead = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
            var back = again.Build(new Dictionary<string, RecodedImage> { [renamed.Key] = new RecodedImage(pngHead, 1, 1, false, ImageCodec.Png) });
            using var third = new HwpPackage(back);
            Check("hwp DocInfo 확장자도 함께 바뀜", third.Images.Any(i => i.Key.Equals(hpng.Key, StringComparison.OrdinalIgnoreCase)),
                  string.Join(",", third.Images.Select(i => i.Key)));
        }
    }
}

Console.WriteLine("[문서: hwp]");
{
    var original = File.ReadAllBytes(Path.Combine(fixtures, "sample.hwp"));
    using var pkg = new HwpPackage(original);
    Check("그림 2개 찾음 (jpg·png)", pkg.Images.Count == 2, $"{pkg.Images.Count}");
    var r = DocumentShrinker.Shrink(pkg, new FakeRecoder(), (long)(original.Length * 0.9));
    Check("줄어듦", r.Status is DocShrinkStatus.Done or DocShrinkStatus.BestEffort && r.Data!.Length < original.Length, $"{r.Status} {r.Data?.Length}/{original.Length}");
    using var again = new HwpPackage(r.Data!);
    Check("다시 열림·그림 수 같음", again.Images.Count == 2);
    Check("바뀐 그림이 그대로 읽힘", again.Images.All(i => i.Data.Length < pkg.Images.First(o => o.Key == i.Key).Data.Length));
    Check("배포용 문서 거부", TestUtil.Throws<DocumentNotSupportedException>(() => new HwpPackage(File.ReadAllBytes(Path.Combine(fixtures, "distribution.hwp")))));
    Check("hwp 아닌 파일 거부", TestUtil.Throws<DocumentNotSupportedException>(() => new HwpPackage(new byte[600])));
}

Console.WriteLine("[문서: PDF]");
var qpdf = Environment.GetEnvironmentVariable("SSOK_QPDF") ?? (File.Exists("/usr/bin/qpdf") ? "/usr/bin/qpdf" : null);
if (qpdf is null) Console.WriteLine("  건너뜀  qpdf 없음 (SSOK_QPDF 설정 필요)");
else
{
    var temp = Path.Combine(Path.GetTempPath(), "ssok-test-pdf");
    var original = File.ReadAllBytes(Path.Combine(fixtures, "sample.pdf"));
    using (var pkg = new PdfPackage(original, qpdf, temp))
    {
        Check("그림 2개 찾음 (화소·ASCII85 JPEG)", pkg.Images.Count == 2, $"{pkg.Images.Count}");
        Check("흑백 JPEG은 1성분", pkg.Images.Any(i => i.Codec == ImageCodec.Jpeg && i.Components == 1));
        var r = DocumentShrinker.Shrink(pkg, new FakeRecoder(), original.Length / 2);
        Check("줄어듦", r.Status == DocShrinkStatus.Done && r.Data!.Length <= original.Length / 2, $"{r.Status} {r.Data?.Length}/{original.Length}");
        using var again = new PdfPackage(r.Data!, qpdf, temp);
        Check("다시 열림·그림 수 같음", again.Images.Count == 2, $"{again.Images.Count}");
        var gray = DocumentShrinker.Shrink(pkg, new FakeRecoder(gray: true), original.Length / 2);
        using var grayAgain = new PdfPackage(gray.Data!, qpdf, temp);
        Check("흑백으로 바꾸면 색 성분 1", grayAgain.Images.All(i => i.Components == 1), string.Join(",", grayAgain.Images.Select(i => i.Components)));
    }
    Check("암호 PDF 거부", TestUtil.Throws<DocumentNotSupportedException>(() => new PdfPackage(File.ReadAllBytes(Path.Combine(fixtures, "encrypted.pdf")), qpdf, temp)));

    // 투명 마스크(/SMask)로 쓰이는 그림은 다시 압축하지 않는다. 마스크가 아닌 흑백 그림은 그대로 줄인다.
    {
        const int n = 300;
        var color = new byte[n * n * 3];
        var gray = new byte[n * n];
        Random.Shared.NextBytes(color);
        Random.Shared.NextBytes(gray);
        var smaskPdf = TestUtil.MakePdf(
            ("<< /Type /Catalog /Pages 2 0 R >>", null),
            ("<< /Type /Pages /Count 1 /Kids [3 0 R] >>", null),
            ("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Resources << /XObject << /Im0 4 0 R /Im1 6 0 R >> >> /Contents 7 0 R >>", null),
            ($"<< /Type /XObject /Subtype /Image /Width {n} /Height {n} /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 5 0 R >>", color),
            ($"<< /Type /XObject /Subtype /Image /Width {n} /Height {n} /ColorSpace /DeviceGray /BitsPerComponent 8 >>", gray),
            ($"<< /Type /XObject /Subtype /Image /Width {n} /Height {n} /ColorSpace /DeviceGray /BitsPerComponent 8 >>", gray),
            ("<< >>", System.Text.Encoding.ASCII.GetBytes("q 300 0 0 300 0 0 cm /Im0 Do /Im1 Do Q")));
        using var masked = new PdfPackage(smaskPdf, qpdf, temp);
        var keys = masked.Images.Select(i => i.Key).ToList();
        Check("투명 마스크는 건드리지 않음", !keys.Contains("obj:5 0 R"), string.Join(",", keys));
        Check("마스크를 쓰는 그림·일반 흑백 그림은 줄임", keys.Contains("obj:4 0 R") && keys.Contains("obj:6 0 R"), string.Join(",", keys));
    }

    // 멈추기: 이미 멈춘 상태면 qpdf를 시작하지 않고, 오래 걸리는 qpdf는 도중에 끈다.
    {
        using var stopped = new CancellationTokenSource();
        stopped.Cancel();
        Check("멈춘 뒤에는 PDF를 열지 않음", TestUtil.Throws<OperationCanceledException>(() => new PdfPackage(original, qpdf, temp, stopped.Token).Dispose()));
        if (OperatingSystem.IsWindows())
        {
            // 60초 동안 아무것도 안 하는 가짜 qpdf
            var slow = Path.Combine(Path.GetTempPath(), "ssok-slow-qpdf-" + Guid.NewGuid().ToString("N") + ".cmd");
            File.WriteAllText(slow, "@ping -n 60 127.0.0.1 > nul\r\n");
            using var soon = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool canceled = TestUtil.Throws<OperationCanceledException>(() => new PdfPackage(original, slow, temp, soon.Token).Dispose());
            Check("PDF 처리 중 멈추기", canceled && sw.Elapsed < TimeSpan.FromSeconds(10), $"{sw.ElapsedMilliseconds}ms");
            try { File.Delete(slow); } catch { }
        }
    }
    Check("임시 폴더 남지 않음", !Directory.Exists(temp) || !Directory.EnumerateDirectories(temp).Any());
}

Console.WriteLine("[업데이트 파일]");
{
    // 받는 양 제한
    var data = new byte[1000];
    Random.Shared.NextBytes(data);
    var read = await UpdateFile.ReadLimitedAsync(new MemoryStream(data), 1000);
    Check("한도 안이면 그대로 받음", read.SequenceEqual(data));
    bool tooBig = false;
    try { await UpdateFile.ReadLimitedAsync(new MemoryStream(data), 999); }
    catch (InvalidDataException) { tooBig = true; }
    Check("한도를 넘으면 멈춤", tooBig);

    var dir = Path.Combine(Path.GetTempPath(), "ssok-update-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var file = Path.Combine(dir, "setup.exe");
        File.WriteAllBytes(file, data);
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));

        using (var locked = await UpdateFile.OpenVerifiedAsync(file, sha.ToLowerInvariant()))
        {
            Check("지문이 맞으면 열림", locked.Position == 0 && locked.Length == data.Length);
            Check("확인 뒤에는 고쳐 쓸 수 없음", TestUtil.Fails(() => File.OpenWrite(file).Dispose()));
            Check("확인 뒤에는 지울 수 없음", TestUtil.Fails(() => File.Delete(file)) && File.Exists(file));
            Check("확인 뒤에도 읽기(실행)는 됨", !TestUtil.Fails(() => new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read).Dispose()));
        }
        Check("닫으면 잠금 풀림", !TestUtil.Fails(() => File.OpenWrite(file).Dispose()));

        bool rejected = false;
        try { (await UpdateFile.OpenVerifiedAsync(file, new string('0', 64))).Dispose(); }
        catch (InvalidDataException) { rejected = true; }
        Check("지문이 다르면 거부", rejected);
        Check("지문이 다르면 파일 지움", !File.Exists(file));
    }
    finally { Directory.Delete(dir, recursive: true); }
}

Console.WriteLine();
Console.WriteLine($"통과 {passed} · 실패 {failed}");
return failed == 0 ? 0 : 1;

// 크기 = 가로×세로×배율² × 품질에 비례하는 가짜 그림. 실제 JPG처럼 품질·크기가 작을수록 작아진다.
sealed class FakeImage(int w, int h, bool lossy, double bytesPerPixelAt100) : IEncodableImage
{
    public int Width => w;
    public int Height => h;
    public bool SupportsQuality => lossy;
    public int Calls { get; private set; }
    public List<(double Scale, int Quality)> Log { get; } = new();

    public byte[] Encode(double scale, int quality)
    {
        Calls++;
        Log.Add((scale, quality));
        double q = lossy ? Math.Clamp(quality, 1, 100) / 100.0 : 1.0;
        long size = (long)(w * scale * h * scale * bytesPerPixelAt100 * q) + 500;
        return new byte[size];
    }
}


static class TestUtil
{
public static bool Throws<T>(Action a) where T : Exception
{
    try { a(); return false; } catch (T) { return true; } catch { return false; }
}

/// <summary>
/// 객체 목록으로 작은 PDF를 만든다. 객체 번호는 1부터 순서대로. data가 있으면 스트림(압축 없음)으로 쓴다.
/// </summary>
public static byte[] MakePdf(params (string Dict, byte[]? Data)[] objects)
{
    var latin1 = System.Text.Encoding.Latin1;
    using var ms = new MemoryStream();
    void Ascii(string s) => ms.Write(latin1.GetBytes(s));
    Ascii("%PDF-1.4\n");
    var offsets = new long[objects.Length];
    for (int i = 0; i < objects.Length; i++)
    {
        offsets[i] = ms.Position;
        var (dict, data) = objects[i];
        if (data is null) { Ascii($"{i + 1} 0 obj\n{dict}\nendobj\n"); continue; }
        // 사전 끝(>>) 앞에 /Length를 넣는다.
        Ascii($"{i + 1} 0 obj\n{dict[..dict.LastIndexOf(">>", StringComparison.Ordinal)]} /Length {data.Length} >>\nstream\n");
        ms.Write(data);
        Ascii("\nendstream\nendobj\n");
    }
    long xref = ms.Position;
    Ascii($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
    foreach (var o in offsets) Ascii($"{o:D10} 00000 n \n");
    Ascii($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    return ms.ToArray();
}

/// <summary>어떤 예외든 나면 true.</summary>
public static bool Fails(Action a)
{
    try { a(); return false; } catch { return true; }
}

/// <summary>zip(hwpx)에서 name 항목을 data로 바꾼다. 없으면 맨 뒤에 더한다. 나머지 항목은 그대로.</summary>
public static byte[] ZipWith(byte[] source, string name, byte[] data)
{
    using var output = new MemoryStream();
    using (var src = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read))
    using (var dst = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
    {
        bool replaced = false;
        foreach (var e in src.Entries)
        {
            var level = e.FullName == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
            using var s = dst.CreateEntry(e.FullName, level).Open();
            if (e.FullName == name) { s.Write(data); replaced = true; }
            else { using var r = e.Open(); r.CopyTo(s); }
        }
        if (!replaced)
        {
            using var s = dst.CreateEntry(name).Open();
            s.Write(data);
        }
    }
    return output.ToArray();
}

public static string FindFixtures()
{
    var env = Environment.GetEnvironmentVariable("SSOK_FIXTURES");
    if (env is not null) return env;
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
    {
        var c = Path.Combine(d.FullName, "tests", "fixtures");
        if (Directory.Exists(c)) return c;
    }
    throw new DirectoryNotFoundException("tests/fixtures");
}
}

// 형식을 JPEG으로 바꾼 결과를 흉내 내는 작은 JPEG 머리.
static class JpegRecoder
{
    public static RecodedImage Make() => new(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1 }, 1, 1, false, ImageCodec.Jpeg);
}

// 단계가 높을수록 그림을 작게 만드는 가짜 압축기. 그림의 첫 바이트(형식 표시)는 유지한다.
sealed class FakeRecoder(bool gray = false) : IImageRecoder
{
    public RecodedImage? Recode(DocImage image, int maxLongSide, int jpegQuality)
    {
        double f = (maxLongSide / 4096.0) * (jpegQuality / 100.0) * 0.6;
        int len = Math.Max(64, (int)(image.Data.Length * f));
        var data = new byte[len];
        var head = image.OutputCodec == ImageCodec.Png
            ? new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            : new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        head.CopyTo(data, 0);
        int w = Math.Max(1, image.Width / 2), h = Math.Max(1, image.Height / 2);
        // PDF 다시 열기 시험에서 화소 수와 맞도록 흑백 결과는 진짜 크기를 흉내 내지 않는다 (JPEG만 쓰므로 상관없음).
        return new RecodedImage(data, w, h, gray);
    }
}

// 글자 부분(fixed) + 크기가 서로 다른 그림들로 된 가짜 문서
sealed class SizedPackage(long fixedBytes, int[] imageSizes) : IDocumentPackage
{
    private readonly List<DocImage> _images = imageSizes
        .Select((s, i) => new DocImage { Key = "i" + i, Codec = ImageCodec.Jpeg, OutputCodec = ImageCodec.Jpeg, Data = new byte[s] }).ToList();
    public long OriginalSize => fixedBytes + _images.Sum(i => (long)i.Data.Length);
    public IReadOnlyList<DocImage> Images => _images;
    public byte[] Build(IReadOnlyDictionary<string, RecodedImage> replacements, CancellationToken cancel = default)
        => new byte[fixedBytes + _images.Sum(i => (long)(replacements.TryGetValue(i.Key, out var r) ? r.Data.Length : i.Data.Length))];
    public void Dispose() { }
}

// 글자 부분(fixed) + 그림 n개로 된 가짜 문서
sealed class FakePackage(long fixedBytes, int images, int imageBytes) : IDocumentPackage
{
    private readonly List<DocImage> _images = Enumerable.Range(0, images)
        .Select(i => new DocImage { Key = "i" + i, Codec = ImageCodec.Jpeg, OutputCodec = ImageCodec.Jpeg, Data = new byte[imageBytes] }).ToList();
    public long OriginalSize => fixedBytes + (long)images * imageBytes;
    public IReadOnlyList<DocImage> Images => _images;
    public long SizeAt(int level)
    {
        var (side, q) = DocumentShrinker.Ladder[level];
        var r = new FakeRecoder();
        return fixedBytes + _images.Sum(i => (long)Math.Min(i.Data.Length, r.Recode(i, side, q)!.Data.Length));
    }
    public byte[] Build(IReadOnlyDictionary<string, RecodedImage> replacements, CancellationToken cancel = default)
        => new byte[fixedBytes + _images.Sum(i => (long)(replacements.TryGetValue(i.Key, out var r) ? r.Data.Length : i.Data.Length))];
    public void Dispose() { }
}
