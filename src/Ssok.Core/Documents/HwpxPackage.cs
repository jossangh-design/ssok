using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Ssok.Core.Documents;

/// <summary>
/// hwpx (OWPML) 문서. zip 묶음이므로 BinData 폴더의 그림만 바꿔 다시 묶는다.
/// - 항목 순서, 이름, 날짜를 그대로 유지하고 mimetype은 맨 앞에 무압축으로 둔다.
/// - 압축 폭탄(풀면 지나치게 커지는 파일)과 이상한 경로를 거부한다.
/// - BMP·GIF·투명 부분 없는 PNG는 JPEG으로 바꿔 크게 줄인다. 이때 그림 파일 이름(확장자)과
///   문서 목록(content.hpf)의 href·media-type만 함께 고친다. 본문은 그림을 id로 가리키므로 그대로 둔다.
/// </summary>
public sealed class HwpxPackage : IDocumentPackage
{
    private const int MaxEntries = 20_000;
    private const long MaxExpandedBytes = 2L * 1024 * 1024 * 1024;
    private const int MaxExpansionRatio = 50;

    private readonly List<EntryData> _entries = new();
    private readonly List<DocImage> _images = new();

    public long OriginalSize { get; }
    public IReadOnlyList<DocImage> Images => _images;

    private sealed record EntryData(string Name, byte[] Data, DateTimeOffset LastWrite, bool WasStored);

    public HwpxPackage(byte[] fileBytes)
    {
        OriginalSize = fileBytes.LongLength;
        long limit = Math.Min(MaxExpandedBytes, Math.Max(64L * 1024 * 1024, fileBytes.LongLength * MaxExpansionRatio));

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(new MemoryStream(fileBytes, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new DocumentNotSupportedException("hwpx 파일이 깨졌거나 다른 형식이에요.");
        }

        using (zip)
        {
            if (zip.Entries.Count > MaxEntries)
                throw new DocumentNotSupportedException("hwpx 안에 파일이 너무 많아서 처리하지 않아요.");

            bool hasMimetype = false;
            long expanded = 0;
            foreach (var entry in zip.Entries)
            {
                ValidateName(entry.FullName);
                if (entry.FullName == "mimetype") hasMimetype = true;

                expanded += entry.Length;
                if (entry.Length < 0 || expanded > limit)
                    throw new DocumentNotSupportedException("풀었을 때 크기가 비정상적으로 커서 처리하지 않아요.");

                var data = ReadAll(entry, limit);
                bool stored = entry.CompressedLength == entry.Length;
                _entries.Add(new EntryData(entry.FullName, data, entry.LastWriteTime, stored));

                if (IsBinData(entry.FullName))
                {
                    var codec = ImageSniffer.Detect(data);
                    if (codec is ImageCodec.Jpeg or ImageCodec.Png or ImageCodec.Bmp or ImageCodec.Gif)
                        _images.Add(new DocImage
                        {
                            Key = entry.FullName, Codec = codec, Data = data,
                            // BMP·GIF는 투명 부분이 있으면 PNG로 (무손실)
                            OutputCodec = codec is ImageCodec.Bmp or ImageCodec.Gif ? ImageCodec.Png : codec,
                            MayBecomeJpeg = codec != ImageCodec.Jpeg,
                        });
                }
            }

            if (!hasMimetype || !_entries.Any(e => e.Name.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase)))
                throw new DocumentNotSupportedException("hwpx 문서 구조가 아니에요.");
        }
    }

    public byte[] Build(IReadOnlyDictionary<string, RecodedImage> replacements)
    {
        // 형식이 바뀐 그림: 새 이름을 정하고 content.hpf를 고친다. 고칠 수 없으면 그 그림은 바꾸지 않는다.
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        var skip = new HashSet<string>(StringComparer.Ordinal);
        var hpfEntry = _entries.FirstOrDefault(e => e.Name.EndsWith("content.hpf", StringComparison.OrdinalIgnoreCase));
        string? hpf = hpfEntry is null ? null : Encoding.UTF8.GetString(hpfEntry.Data);
        var taken = new HashSet<string>(_entries.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var img in _images)
        {
            if (!replacements.TryGetValue(img.Key, out var r)) continue;
            var newCodec = r.CodecFor(img);
            if (newCodec == img.Codec) continue;
            var newName = PlanName(img.Key, RecodedImage.ExtensionOf(newCodec), taken);
            if (hpf is null || !TryRetarget(ref hpf, img.Key, newName, newCodec)) { skip.Add(img.Key); continue; }
            renames[img.Key] = newName;
            taken.Add(newName);
        }
        byte[]? newHpf = hpf is not null && renames.Count > 0 ? Encoding.UTF8.GetBytes(hpf) : null;
        // 원래 파일에 BOM이 있었으면 유지한다.
        if (newHpf is not null && hpfEntry!.Data.Length >= 3 && hpfEntry.Data[0] == 0xEF && hpfEntry.Data[1] == 0xBB && hpfEntry.Data[2] == 0xBF
            && !(newHpf.Length >= 3 && newHpf[0] == 0xEF))
            newHpf = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(newHpf).ToArray();

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            // mimetype은 반드시 첫 항목, 무압축 (OCF 규칙)
            foreach (var e in _entries.Where(e => e.Name == "mimetype").Concat(_entries.Where(e => e.Name != "mimetype")))
            {
                var data = !skip.Contains(e.Name) && replacements.TryGetValue(e.Name, out var r) ? r.Data
                         : newHpf is not null && ReferenceEquals(e, hpfEntry) ? newHpf
                         : e.Data;
                var name = renames.TryGetValue(e.Name, out var renamed) ? renamed : e.Name;
                var level = e.Name == "mimetype" ? CompressionLevel.NoCompression
                          : IsBinData(e.Name) ? CompressionLevel.Fastest   // 그림은 이미 압축돼 있어 더 줄지 않음
                          : CompressionLevel.Optimal;
                var created = zip.CreateEntry(name, level);
                try { created.LastWriteTime = e.LastWrite; } catch (ArgumentOutOfRangeException) { }
                using var s = created.Open();
                s.Write(data);
            }
        }
        return output.ToArray();
    }

    /// <summary>BinData/image3.bmp → BinData/image3.jpg (겹치면 image3_1.jpg …)</summary>
    private static string PlanName(string oldName, string ext, HashSet<string> taken)
    {
        int slash = oldName.LastIndexOf('/');
        int dot = oldName.LastIndexOf('.');
        var stem = dot > slash ? oldName[..dot] : oldName;
        var name = $"{stem}.{ext}";
        for (int n = 1; taken.Contains(name); n++) name = $"{stem}_{n}.{ext}";
        return name;
    }

    /// <summary>content.hpf에서 그 그림을 가리키는 항목(opf:item) 하나를 찾아 href와 media-type을 바꾼다.</summary>
    private static bool TryRetarget(ref string hpf, string oldHref, string newHref, ImageCodec codec)
    {
        var hrefAttr = "href=\"" + oldHref + "\"";
        var items = Regex.Matches(hpf, @"<[A-Za-z0-9_]*:?item\b[^>]*>")
            .Where(m => m.Value.Contains(hrefAttr, StringComparison.Ordinal)).ToList();
        if (items.Count != 1) return false;
        var tag = items[0].Value;
        var mime = codec == ImageCodec.Jpeg ? "image/jpg" : "image/png";
        var newTag = tag.Replace(hrefAttr, "href=\"" + newHref + "\"", StringComparison.Ordinal);
        newTag = Regex.Replace(newTag, "media-type=\"[^\"]*\"", "media-type=\"" + mime + "\"");
        hpf = hpf[..items[0].Index] + newTag + hpf[(items[0].Index + items[0].Length)..];
        return true;
    }

    private static bool IsBinData(string name) => name.StartsWith("BinData/", StringComparison.OrdinalIgnoreCase);

    private static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 512
            || name.StartsWith('/') || name.StartsWith('\\') || name.Contains(':')
            || name.Split('/', '\\').Any(part => part == ".."))
            throw new DocumentNotSupportedException("hwpx 안에 이상한 경로가 있어서 처리하지 않아요.");
    }

    private static byte[] ReadAll(ZipArchiveEntry entry, long limit)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream(entry.Length is > 0 and < int.MaxValue ? (int)entry.Length : 0);
        var buffer = new byte[81920];
        long total = 0;
        int n;
        while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += n;
            // 머리말에 적힌 크기를 믿지 않고 실제로 풀린 양으로 다시 확인한다.
            if (total > limit || total > entry.Length + 1)
                throw new DocumentNotSupportedException("풀었을 때 크기가 비정상적으로 커서 처리하지 않아요.");
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    public void Dispose() { }
}
