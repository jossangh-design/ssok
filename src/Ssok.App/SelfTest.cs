using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ssok.Core;
using Ssok.Core.Documents;

namespace Ssok.App;

/// <summary>
/// 빌드한 프로그램이 실제 윈도우에서 제대로 도는지 확인한다 (WIC 그림 처리, qpdf, hwp·hwpx).
/// 결과는 글자 파일로 남기고, 실패가 있으면 종료 코드 1.
/// 사용자 파일은 건드리지 않고, 시험 파일 복사본만 임시 폴더에서 다룬다.
/// </summary>
internal static class SelfTest
{
    public static int Run(string fixturesDir, string reportPath)
    {
        var log = new StringBuilder();
        int failed = 0;
        void Check(string name, bool ok, string detail = "")
        {
            log.AppendLine($"{(ok ? "PASS" : "FAIL")} {name} {detail}");
            if (!ok) failed++;
        }
        void Guard(string name, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                log.AppendLine($"FAIL {name} 예외: {ex.GetType().Name}: {ex.Message}");
                failed++;
            }
        }

        var work = Path.Combine(Path.GetTempPath(), "Ssok-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            // 1) 사진: 큰 그림을 만들어 300KB로 줄이기
            Guard("photo", () =>
            {
                var jpg = MakePhoto(3000, 2000);
                var path = Path.Combine(work, "photo.jpg");
                File.WriteAllBytes(path, jpg);
                var o = ImageWork.Shrink(path, FileKind.Jpeg, new WorkOptions(300 * 1024, PhotoFormat.Original, true, false));
                Check("사진 줄이기", o.Result == WorkResult.Done && o.ResultBytes <= 300 * 1024, $"{o.Result} {o.ResultBytes} {o.Message}");
                Check("원본 그대로", File.ReadAllBytes(path).AsSpan().SequenceEqual(jpg));
                if (o.ResultPath is not null)
                {
                    var f = BitmapFrame.Create(new MemoryStream(File.ReadAllBytes(o.ResultPath)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    Check("결과 그림 열림", f.PixelWidth > 0, $"{f.PixelWidth}x{f.PixelHeight}");
                }
            });

            // 1-1) 화면과 같은 흐름: 보관 폴더에 줄여 두고 → 저장 단추로 고른 폴더와 ZIP에 저장
            Guard("hold-and-save", () =>
            {
                var jpg = MakePhoto(2000, 1500);
                var path = Path.Combine(work, "hold.jpg");
                File.WriteAllBytes(path, jpg);
                var hold = Path.Combine(work, "hold-area");
                Directory.CreateDirectory(hold);
                var o = ImageWork.Shrink(path, FileKind.Jpeg, new WorkOptions(200 * 1024, PhotoFormat.Original, true, false), hold);
                Check("보관 폴더에만 씀", o.ResultPath is not null && Path.GetDirectoryName(o.ResultPath) == hold
                      && !File.Exists(Path.Combine(work, "hold_쏙.jpg")), o.ResultPath ?? o.Message);
                if (o.ResultPath is null) return;

                var dest = Path.Combine(work, "저장한 곳");
                Directory.CreateDirectory(dest);
                var saved = SafeOutput.CopyInto(o.ResultPath, dest, Path.GetFileName(o.ResultPath));
                Check("폴더에 저장", Path.GetFileName(saved) == "hold_쏙.jpg" && new FileInfo(saved).Length == o.ResultBytes, saved);

                var zipPath = Path.Combine(work, "결과.zip");
                SafeOutput.WriteAtomic(zipPath, fs =>
                {
                    using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true);
                    System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, o.ResultPath, "hold_쏙.jpg", System.IO.Compression.CompressionLevel.Fastest);
                }, overwrite: true);
                using var check = System.IO.Compression.ZipFile.OpenRead(zipPath);
                Check("ZIP으로 저장", check.Entries.Count == 1 && check.Entries[0].FullName == "hold_쏙.jpg"
                      && check.Entries[0].Length == o.ResultBytes, $"{check.Entries.Count}");
            });

            // 1-2) 투명 PNG를 JPG로 바꾸기 (흰 바탕), 이미 작아도 형식은 바뀌어야 함
            Guard("png-to-jpg", () =>
            {
                var path = Path.Combine(work, "logo.png");
                File.WriteAllBytes(path, MakeTransparentPng(400, 300));
                var o = ImageWork.Shrink(path, FileKind.Png, new WorkOptions(5 * 1024 * 1024, PhotoFormat.Jpg, true, false));
                Check("PNG→JPG 저장", o.Result == WorkResult.Done && o.ResultPath?.EndsWith(".jpg") == true, $"{o.Result} {o.Message}");
                if (o.ResultPath is not null)
                {
                    var f = BitmapFrame.Create(new MemoryStream(File.ReadAllBytes(o.ResultPath)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    var c = new FormatConvertedBitmap(f, PixelFormats.Bgr24, null, 0);
                    var px = new byte[3];
                    c.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), px, 3, 0);
                    Check("투명 부분은 흰색", px[0] > 240 && px[1] > 240 && px[2] > 240, $"{px[0]},{px[1]},{px[2]}");
                }
            });

            // 1-3) PDF 흑백으로 바꾸기
            Guard("pdf-gray", () =>
            {
                var path = Path.Combine(work, "gray.pdf");
                File.Copy(Path.Combine(fixturesDir, "sample.pdf"), path);
                var o = ImageWork.Shrink(path, FileKind.Pdf, new WorkOptions(50 * 1024 * 1024, PhotoFormat.Original, true, true));
                Check("PDF 흑백 저장", o.Result == WorkResult.Done && o.ResultPath is not null, $"{o.Result} {o.Message}");
                if (o.ResultPath is not null)
                {
                    using var p = new PdfPackage(File.ReadAllBytes(o.ResultPath), ImageWork.QpdfPath, ImageWork.TempRoot);
                    Check("PDF 그림 모두 흑백", p.Images.All(i => i.Components == 1), string.Join(",", p.Images.Select(i => i.Components)));
                }
            });

            // 1-4) 그림만 줄여서는 못 맞추는 PDF: 쪽을 그림으로 바꿔 맞추기 (윈도우 내장 PDF 렌더러)
            Guard("pdf-pages", () =>
            {
                // 먼저 '그림만 줄이기'로 갈 수 있는 가장 작은 크기를 잰다 (자가 시험 전용 끄기 옵션).
                // 그림 압축이 좋아져도 시험이 깨지지 않도록 목표를 그보다 작게 잡는다.
                File.Copy(Path.Combine(fixturesDir, "sample.pdf"), Path.Combine(work, "pages-off.pdf"));
                var off = ImageWork.Shrink(Path.Combine(work, "pages-off.pdf"), FileKind.Pdf,
                    new WorkOptions(1024, PhotoFormat.Original, true, false, false));
                Check("그림만 줄이면 한계가 있음", off.Result == WorkResult.Partial && off.ResultBytes is > 1024
                      && !off.Message.Contains("검색"), $"{off.Result} {off.ResultBytes} {off.Message}");
                long imageFloor = off.ResultBytes ?? long.MaxValue;

                var path = Path.Combine(work, "pages.pdf");
                File.Copy(Path.Combine(fixturesDir, "sample.pdf"), path);
                int pagesBefore;
                using (var r0 = PdfRasterizer.Open(path)) pagesBefore = r0.PageCount;

                // 쪽을 그림으로 바꾸는 방식만 따로: 가장 작게 만든 크기를 재고, 그 크기를 목표로 하면 맞춰야 한다.
                var floor = PdfPageShrink.Run(path, 1, false, null, default);
                Check("쪽 그림: 가장 작은 단계까지 감", !floor.Reached && floor.Data.Length > 0, $"{floor.Data.Length} {floor.Dpi}dpi");
                var fit = PdfPageShrink.Run(path, floor.Data.Length, false, null, default);
                Check("쪽을 그림으로 바꿔 목표 맞춤", fit.Reached && fit.Data.Length <= floor.Data.Length && fit.Pages == pagesBefore,
                      $"목표 {floor.Data.Length} → {fit.Data.Length} {fit.Dpi}dpi");

                // 전체 흐름: 두 방식 모두 못 맞추는 목표면 둘 중 더 작은 결과를 고른다.
                long target = Math.Min(imageFloor, floor.Data.Length) - 1;
                int reports = 0;
                var o = ImageWork.Shrink(path, FileKind.Pdf, new WorkOptions(target, PhotoFormat.Original, true, false),
                    null, default, (_, _) => reports++);
                Check("더 작은 쪽을 고름", o.Result == WorkResult.Partial && o.ResultBytes <= Math.Min(imageFloor, floor.Data.Length),
                      $"목표 {target} → {o.Result} {o.ResultBytes} {o.Message}");
                Check("진행 알림", reports > 0, $"{reports}");
                if (o.ResultPath is not null)
                {
                    using var r1 = PdfRasterizer.Open(o.ResultPath);
                    Check("쪽 수 그대로", r1.PageCount == pagesBefore, $"{pagesBefore}->{r1.PageCount}");
                    var img = r1.Render(0, 30);
                    Check("결과 1쪽 그려짐", img.PixelWidth > 0, $"{img.PixelWidth}x{img.PixelHeight}");
                }

                // 멈추기
                using var cts = new CancellationTokenSource();
                cts.Cancel();
                var c = ImageWork.Shrink(path, FileKind.Pdf, new WorkOptions(target, PhotoFormat.Original, true, false), null, cts.Token);
                Check("멈추기", c.Result == WorkResult.Canceled, $"{c.Result}");
            });

            // 1-5) 한글 문서 속 BMP 그림: JPEG으로 바꾸고 문서 목록(content.hpf)도 함께 고친다.
            Guard("hwpx-bmp", () =>
            {
                var src = File.ReadAllBytes(Path.Combine(fixturesDir, "sample.hwpx"));
                var path = Path.Combine(work, "bmp.hwpx");
                string? oldName = null;
                using (var inZip = new System.IO.Compression.ZipArchive(new MemoryStream(src), System.IO.Compression.ZipArchiveMode.Read))
                using (var fs = File.Create(path))
                using (var outZip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
                {
                    foreach (var e in inZip.Entries)
                    {
                        using var es = e.Open();
                        using var buf = new MemoryStream();
                        es.CopyTo(buf);
                        var data = buf.ToArray();
                        // PNG 그림 하나를 같은 그림의 큰 BMP로 바꿔 넣는다 (한글이 BMP로 저장한 문서 흉내).
                        if (oldName is null && e.FullName.StartsWith("BinData/") && ImageSniffer.Detect(data) == ImageCodec.Png)
                        {
                            oldName = e.FullName[..e.FullName.LastIndexOf('.')] + ".bmp";
                            var photo = BitmapSource.Create(1200, 900, 96, 96, PixelFormats.Bgr24, null, MakePhotoPixels(1200, 900), 1200 * 3);
                            var enc = new BmpBitmapEncoder();
                            enc.Frames.Add(BitmapFrame.Create(photo));
                            using var bm = new MemoryStream();
                            enc.Save(bm);
                            data = bm.ToArray();
                            var ce = outZip.CreateEntry(oldName, System.IO.Compression.CompressionLevel.Fastest);
                            using (var cs = ce.Open()) cs.Write(data);
                            continue;
                        }
                        var created = outZip.CreateEntry(e.FullName, e.FullName == "mimetype"
                            ? System.IO.Compression.CompressionLevel.NoCompression : System.IO.Compression.CompressionLevel.Optimal);
                        using var os = created.Open();
                        os.Write(data);
                    }
                }
                Check("BMP 넣은 시험 문서 만듦", oldName is not null, oldName ?? "");
                if (oldName is null) return;
                // 문서 목록의 이름도 .bmp로 맞춘다.
                FixHpf(path, oldName[..oldName.LastIndexOf('.')] + ".png", oldName, "image/bmp");

                long before = new FileInfo(path).Length;
                var o = ImageWork.Shrink(path, FileKind.Hwpx, new WorkOptions(before / 4, PhotoFormat.Original, true, false));
                Check("BMP 든 hwpx 목표 맞춤", o.Result == WorkResult.Done && o.ResultBytes <= before / 4, $"{before}->{o.ResultBytes} {o.Result} {o.Message}");
                if (o.ResultPath is null) return;
                using var z = new System.IO.Compression.ZipArchive(new MemoryStream(File.ReadAllBytes(o.ResultPath)), System.IO.Compression.ZipArchiveMode.Read);
                var jpgName = oldName[..oldName.LastIndexOf('.')] + ".jpg";
                var hpfEntry = z.Entries.First(e => e.FullName.EndsWith("content.hpf"));
                string hpf;
                using (var r = new StreamReader(hpfEntry.Open())) hpf = r.ReadToEnd();
                Check("BMP가 JPG로 바뀜", z.GetEntry(jpgName) is not null && z.GetEntry(oldName) is null, jpgName);
                Check("문서 목록도 고침", hpf.Contains("href=\"" + jpgName + "\"") && !hpf.Contains(oldName), "");
                using var reopened = new HwpxPackage(File.ReadAllBytes(o.ResultPath));
                Check("BMP 든 hwpx 다시 열림", reopened.Images.Any(i => i.Key == jpgName && i.Codec == ImageCodec.Jpeg), $"images={reopened.Images.Count}");
            });

            // 2) 문서들: 원본의 60%를 목표로
            foreach (var name in new[] { "sample.hwpx", "sample.hwp", "sample.pdf" })
            {
                Guard(name, () =>
                {
                    var src = Path.Combine(fixturesDir, name);
                    var path = Path.Combine(work, name);
                    File.Copy(src, path);
                    long before = new FileInfo(path).Length;
                    var kind = FileKinds.Detect(path);
                    var o = ImageWork.Shrink(path, kind, new WorkOptions((long)(before * 0.6), PhotoFormat.Original, true, false));
                    Check($"{name} 줄이기", o.Result is WorkResult.Done or WorkResult.Partial && o.ResultBytes < before,
                          $"{o.Result} {before}->{o.ResultBytes} {o.Message}");
                    if (o.ResultPath is null) return;

                    var outBytes = File.ReadAllBytes(o.ResultPath);
                    using IDocumentPackage reopened = kind switch
                    {
                        FileKind.Hwpx => new HwpxPackage(outBytes),
                        FileKind.Hwp => new HwpPackage(outBytes),
                        _ => new PdfPackage(outBytes, ImageWork.QpdfPath, ImageWork.TempRoot),
                    };
                    Check($"{name} 다시 열림", reopened.Images.Count > 0, $"images={reopened.Images.Count}");
                    foreach (var img in reopened.Images.Where(i => i.Codec is ImageCodec.Jpeg or ImageCodec.Png))
                    {
                        var f = BitmapFrame.Create(new MemoryStream(img.Data), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        Check($"{name} 그림 {img.Key} 열림", f.PixelWidth > 0, $"{f.PixelWidth}x{f.PixelHeight}");
                    }
                });
            }

            // 3) 거부해야 하는 문서
            Guard("refuse", () =>
            {
                foreach (var name in new[] { "distribution.hwp", "encrypted.pdf" })
                {
                    var path = Path.Combine(work, name);
                    File.Copy(Path.Combine(fixturesDir, name), path);
                    var o = ImageWork.Shrink(path, FileKinds.Detect(path), new WorkOptions(10 * 1024, PhotoFormat.Original, true, false));
                    Check($"{name} 거부", o.Result == WorkResult.Failed && o.ResultPath is null, o.Message);
                }
            });

            Check("qpdf 있음", File.Exists(ImageWork.QpdfPath));
        }
        catch (Exception ex)
        {
            log.AppendLine("FAIL 예외 " + ex);
            failed++;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { }
        }

        log.AppendLine(failed == 0 ? "SELFTEST: OK" : $"SELFTEST: FAIL ({failed})");
        File.WriteAllText(reportPath, log.ToString(), new UTF8Encoding(true));
        return failed == 0 ? 0 : 1;
    }

    /// <summary>시험 문서의 content.hpf에서 그림 이름과 형식을 바꾼다.</summary>
    private static void FixHpf(string hwpxPath, string fromHref, string toHref, string mime)
    {
        using var z = System.IO.Compression.ZipFile.Open(hwpxPath, System.IO.Compression.ZipArchiveMode.Update);
        var entry = z.Entries.First(e => e.FullName.EndsWith("content.hpf"));
        string hpf;
        using (var r = new StreamReader(entry.Open())) hpf = r.ReadToEnd();
        var tagStart = hpf.IndexOf("href=\"" + fromHref + "\"", StringComparison.Ordinal);
        if (tagStart < 0) return;
        int open = hpf.LastIndexOf('<', tagStart), close = hpf.IndexOf('>', tagStart);
        var tag = hpf[open..(close + 1)];
        var fixedTag = System.Text.RegularExpressions.Regex.Replace(tag.Replace(fromHref, toHref), "media-type=\"[^\"]*\"", "media-type=\"" + mime + "\"");
        hpf = hpf[..open] + fixedTag + hpf[(close + 1)..];
        entry.Delete();
        var created = z.CreateEntry("Contents/content.hpf");
        using var w = new StreamWriter(created.Open(), new UTF8Encoding(false));
        w.Write(hpf);
    }

    private static byte[] MakePhotoPixels(int w, int h)
    {
        var px = new byte[w * h * 3];
        var rnd = new Random(7);
        for (int i = 0; i < px.Length; i++) px[i] = (byte)((i / 3 % w) * 255 / w + rnd.Next(40));
        return px;
    }

    private static byte[] MakeTransparentPng(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bool inside = x > w / 4 && x < w * 3 / 4 && y > h / 4 && y < h * 3 / 4;
                px[i] = 40; px[i + 1] = 140; px[i + 2] = 100; px[i + 3] = (byte)(inside ? 255 : 0);
            }
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static byte[] MakePhoto(int w, int h)
    {
        var px = new byte[w * h * 3];
        var rnd = new Random(1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 3;
                px[i] = (byte)(x * 255 / w);
                px[i + 1] = (byte)(y * 255 / h);
                px[i + 2] = (byte)((x + y) % 256 ^ rnd.Next(40));
            }
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Rgb24, null, px, w * 3);
        var enc = new JpegBitmapEncoder { QualityLevel = 95 };
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
