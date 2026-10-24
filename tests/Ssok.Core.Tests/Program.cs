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
    Check("임시 폴더 남지 않음", !Directory.Exists(temp) || !Directory.EnumerateDirectories(temp).Any());
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
    public byte[] Build(IReadOnlyDictionary<string, RecodedImage> replacements)
        => new byte[fixedBytes + _images.Sum(i => (long)(replacements.TryGetValue(i.Key, out var r) ? r.Data.Length : i.Data.Length))];
    public void Dispose() { }
}
