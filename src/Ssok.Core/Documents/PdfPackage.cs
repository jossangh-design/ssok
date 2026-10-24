using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ssok.Core.Documents;

/// <summary>
/// PDF 문서. 읽고 쓰는 일은 qpdf(Apache-2.0)가 하고, 쏙은 그림만 다시 압축해서 바꿔 끼운다.
/// 1) qpdf --json-output 으로 객체 목록과 스트림 데이터를 임시 폴더에 꺼낸다.
/// 2) JPEG 그림과 압축 안 된 8비트 RGB·흑백 그림을 찾는다.
/// 3) 바꾼 그림만 담은 JSON을 qpdf --update-from-json 으로 적용해 새 PDF를 쓴다.
/// 암호가 걸린 PDF, CMYK·특수 색 공간 그림은 건드리지 않는다.
/// </summary>
public sealed class PdfPackage : IDocumentPackage
{
    private static readonly TimeSpan QpdfTimeout = TimeSpan.FromMinutes(3);
    private const int MinRawPixels = 300 * 300; // 작은 아이콘·로고는 그대로 둔다 (선명함 유지)

    private readonly string _qpdf;
    private readonly string _workDir;
    private readonly string _inputPath;
    private readonly Dictionary<string, JsonObject> _dicts = new();
    private readonly List<DocImage> _images = new();
    private int _buildCounter;

    public long OriginalSize { get; }
    public IReadOnlyList<DocImage> Images => _images;

    /// <param name="qpdfPath">qpdf 실행 파일 경로</param>
    /// <param name="tempRoot">임시 작업 폴더를 만들 곳 (보통 %TEMP%\Ssok)</param>
    public PdfPackage(byte[] fileBytes, string qpdfPath, string tempRoot)
    {
        OriginalSize = fileBytes.LongLength;
        _qpdf = qpdfPath;
        if (!File.Exists(_qpdf))
            throw new DocumentNotSupportedException("PDF 처리 부품(qpdf)을 찾을 수 없어요. 쏙을 다시 설치해 주세요.");

        _workDir = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
        try
        {
            _inputPath = Path.Combine(_workDir, "in.pdf");
            File.WriteAllBytes(_inputPath, fileBytes);

            // 종료 코드 0 = 암호 있음, 2 = 없음
            var enc = Run("--is-encrypted", _inputPath);
            if (enc.ExitCode == 0)
                throw new DocumentNotSupportedException("암호가 걸린 PDF는 바꾸지 않아요.");

            var jsonPath = Path.Combine(_workDir, "objects.json");
            var streamPrefix = Path.Combine(_workDir, "s");
            var r = Run("--json-output=2", "--json-stream-data=file", $"--json-stream-prefix={streamPrefix}",
                        "--decode-level=generalized", _inputPath, jsonPath);
            if (r.ExitCode is not (0 or 3))
                throw new DocumentNotSupportedException("PDF가 깨졌거나 읽을 수 없는 형식이에요.");

            ReadImages(jsonPath);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void ReadImages(string jsonPath)
    {
        JsonNode? root;
        using (var fs = File.OpenRead(jsonPath)) root = JsonNode.Parse(fs);
        var objects = root?["qpdf"]?[1] as JsonObject
                      ?? throw new DocumentNotSupportedException("PDF 구조를 읽지 못했어요.");

        foreach (var (key, value) in objects)
        {
            if (value?["stream"] is not JsonObject stream || stream["dict"] is not JsonObject dict) continue;
            if (Str(dict["/Subtype"]) != "/Image") continue;
            if (dict.ContainsKey("/ImageMask") || dict.ContainsKey("/Decode") || dict.ContainsKey("/SMaskInData")) continue;
            if (stream["datafile"]?.GetValue<string>() is not { } dataFile || !File.Exists(dataFile)) continue;

            int width = Int(dict["/Width"]), height = Int(dict["/Height"]), bpc = Int(dict["/BitsPerComponent"]);
            int components = Components(dict["/ColorSpace"], objects);
            if (width <= 0 || height <= 0 || components is not (1 or 3)) continue;

            var filters = Filters(dict["/Filter"]);
            byte[] data = File.ReadAllBytes(dataFile);
            DocImage? image = null;

            if (filters.Count >= 1 && filters[^1] == "/DCTDecode")
            {
                // qpdf는 DCT 앞에 붙은 ASCII85·ASCIIHex·Flate 단계를 풀지 않고 그대로 준다.
                if (filters.Count > 1)
                {
                    var pre = PdfFilters.Decode(data, filters.Take(filters.Count - 1));
                    if (pre is null) continue;
                    data = pre;
                }
                if (ImageSniffer.Detect(data) != ImageCodec.Jpeg) continue;
                image = new DocImage { Key = key, Codec = ImageCodec.Jpeg, OutputCodec = ImageCodec.Jpeg, Data = data,
                                       Width = width, Height = height, Components = components };
            }
            // decode-level=generalized 이므로 Flate·LZW 등은 이미 풀린 화소로 나온다.
            // 풀리지 않은 필터(JBIG2, CCITT, JPX)가 남아 있으면 qpdf가 dict에 그대로 둔다.
            else if (filters.Count == 0 && bpc == 8 && (long)width * height >= MinRawPixels
                     && data.LongLength == (long)width * height * components)
            {
                image = new DocImage { Key = key, Codec = components == 3 ? ImageCodec.RawRgb8 : ImageCodec.RawGray8,
                                       OutputCodec = ImageCodec.Jpeg, Data = data,
                                       Width = width, Height = height, Components = components };
            }

            if (image is null) continue;
            _dicts[key] = (JsonObject)dict.DeepClone();
            _images.Add(image);
        }
    }

    public byte[] Build(IReadOnlyDictionary<string, RecodedImage> replacements)
    {
        int n = Interlocked.Increment(ref _buildCounter);
        var dir = Path.Combine(_workDir, "build" + n);
        Directory.CreateDirectory(dir);

        var updates = new JsonObject();
        int i = 0;
        foreach (var (key, image) in replacements)
        {
            var file = Path.Combine(dir, $"img{i++}.jpg");
            File.WriteAllBytes(file, image.Data);

            var dict = (JsonObject)_dicts[key].DeepClone();
            dict["/Filter"] = "/DCTDecode";
            dict["/Width"] = image.Width;
            dict["/Height"] = image.Height;
            dict["/BitsPerComponent"] = 8;
            if (image.Gray) dict["/ColorSpace"] = "/DeviceGray";
            dict.Remove("/DecodeParms");
            dict.Remove("/Length");

            updates[key] = new JsonObject
            {
                ["stream"] = new JsonObject { ["dict"] = dict, ["datafile"] = file },
            };
        }

        var doc = new JsonObject
        {
            ["qpdf"] = new JsonArray(
                new JsonObject { ["jsonversion"] = 2, ["pushedinheritedpageresources"] = false, ["calledgetallpages"] = false, ["maxobjectid"] = 0 },
                updates),
        };
        var updatePath = Path.Combine(dir, "update.json");
        File.WriteAllText(updatePath, doc.ToJsonString(), new UTF8Encoding(false));

        var outPath = Path.Combine(dir, "out.pdf");
        var r = Run(_inputPath, $"--update-from-json={updatePath}",
                    "--object-streams=generate", "--compress-streams=y", outPath);
        if (r.ExitCode is not (0 or 3) || !File.Exists(outPath))
            throw new IOException("qpdf 실행 실패 (exit " + r.ExitCode + ")");

        var bytes = File.ReadAllBytes(outPath);
        try { Directory.Delete(dir, recursive: true); } catch { }
        return bytes;
    }

    // ── 도우미 ─────────────────────────────────────────────────

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int Int(JsonNode? n)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<double>(out var d)) return (int)d;
            if (v.TryGetValue<long>(out var l) && l is > 0 and < int.MaxValue) return (int)l;
        }
        return 0;
    }

    private static List<string> Filters(JsonNode? n) => n switch
    {
        null => new List<string>(),
        JsonArray a => a.Select(Str).Where(s => s is not null).Select(s => s!).ToList(),
        _ => Str(n) is { } s ? new List<string> { s } : new List<string>(),
    };

    /// <summary>색 성분 수. 모르는 색 공간이면 0 (= 건드리지 않음).</summary>
    private static int Components(JsonNode? cs, JsonObject objects)
    {
        switch (Str(cs))
        {
            case "/DeviceRGB": case "/CalRGB": return 3;
            case "/DeviceGray": case "/CalGray": return 1;
            case { } s when s.EndsWith(" R"): return Components(objects[$"obj:{s}"]?["value"], objects);
        }
        if (cs is JsonArray arr && arr.Count == 2 && Str(arr[0]) == "/ICCBased" && Str(arr[1]) is { } r && r.EndsWith(" R"))
        {
            var n = Int(objects[$"obj:{r}"]?["stream"]?["dict"]?["/N"]);
            return n is 1 or 3 ? n : 0;
        }
        if (cs is JsonArray cal && cal.Count >= 1 && Str(cal[0]) is "/CalRGB") return 3;
        if (cs is JsonArray calg && calg.Count >= 1 && Str(calg[0]) is "/CalGray") return 1;
        return 0;
    }

    private (int ExitCode, string Output) Run(params string[] args)
    {
        var psi = new ProcessStartInfo(_qpdf)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _workDir,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new IOException("qpdf를 시작하지 못했습니다.");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(QpdfTimeout))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new DocumentNotSupportedException("PDF 처리 시간이 너무 오래 걸려서 멈췄어요.");
        }
        p.WaitForExit();
        return (p.ExitCode, stdout.Result + stderr.Result);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, recursive: true); }
        catch { /* 다음 실행 때 정리 */ }
    }
}
