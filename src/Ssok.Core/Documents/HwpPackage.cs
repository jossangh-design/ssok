using System.IO.Compression;
using System.Text;
using OpenMcdf;

namespace Ssok.Core.Documents;

/// <summary>
/// hwp(한/글 5.x, 복합 파일) 문서. BinData 저장소의 그림 스트림만 다시 압축해서 바꾼다.
/// - 그림마다 원래 압축(deflate) 여부를 그대로 지킨다.
/// - 암호·배포용·DRM·전자서명 문서는 건드리지 않는다.
/// - BMP·GIF·투명 부분 없는 PNG는 JPEG으로 바꿔 크게 줄인다. 이때 DocInfo에 적힌 확장자(세 글자)와
///   BinData 스트림 이름만 함께 고친다. 본문은 그림을 번호로 가리키므로 그대로 둔다.
/// 본 제품은 한글과컴퓨터의 한/글 문서 파일(.hwp) 공개 문서를 참고하여 개발하였습니다.
/// </summary>
public sealed class HwpPackage : IDocumentPackage
{
    private const string Signature = "HWP Document File";
    private const long MaxInflatedBytes = 300L * 1024 * 1024;

    // FileHeader 속성 비트: 1 암호, 2 배포용, 4 DRM, 7 전자서명, 8 공인인증서 암호화, 10 인증서 DRM
    private const uint ProtectedMask = (1u << 1) | (1u << 2) | (1u << 4) | (1u << 7) | (1u << 8) | (1u << 10);

    private const int TagBinData = 0x12; // HWPTAG_BIN_DATA (HWPTAG_BEGIN + 2)

    private readonly byte[] _original;
    private readonly List<DocImage> _images = new();
    private readonly Dictionary<string, bool> _compressed = new();

    /// <summary>문서 전체 압축 여부 (FileHeader 속성 비트 0). DocInfo도 이 설정을 따른다.</summary>
    private bool _docCompressed;

    /// <summary>BinData 스트림 이름 → DocInfo 안에서 확장자 글자가 시작하는 위치와 글자 수 (풀린 DocInfo 기준).</summary>
    private readonly Dictionary<string, (int Offset, int Length)> _extAt = new(StringComparer.OrdinalIgnoreCase);

    public long OriginalSize => _original.LongLength;
    public IReadOnlyList<DocImage> Images => _images;

    public HwpPackage(byte[] fileBytes)
    {
        _original = fileBytes;
        try
        {
            using var root = RootStorage.Open(new MemoryStream(fileBytes, writable: false));
            _docCompressed = ReadHeader(root);
            ReadBinDataTable(root);

            if (!root.TryOpenStorage("BinData", out var bin)) return; // 그림 없는 문서

            foreach (var entry in bin.EnumerateEntries().Where(e => e.Type == EntryType.Stream).ToList())
            {
                byte[] raw;
                using (var s = bin.OpenStream(entry.Name))
                {
                    if (s.Length > MaxInflatedBytes) continue;
                    raw = new byte[s.Length];
                    s.ReadExactly(raw);
                }

                var (data, wasCompressed) = Unwrap(raw);
                if (data is null) continue;

                var codec = ImageSniffer.Detect(data);
                if (codec is not (ImageCodec.Jpeg or ImageCodec.Png or ImageCodec.Bmp or ImageCodec.Gif)) continue;

                // 형식을 바꾸려면 DocInfo의 확장자를 같은 글자 수(세 글자)로 고칠 수 있어야 한다.
                bool canRename = _extAt.TryGetValue(entry.Name, out var ext) && ext.Length == 3;
                // BMP·GIF는 형식을 못 바꾸면 줄일 방법이 없다 (같은 형식으로 다시 쓰면 커진다).
                if (codec is ImageCodec.Bmp or ImageCodec.Gif && !canRename) continue;

                _compressed[entry.Name] = wasCompressed;
                _images.Add(new DocImage
                {
                    Key = entry.Name, Codec = codec, Data = data,
                    OutputCodec = codec is ImageCodec.Bmp or ImageCodec.Gif ? ImageCodec.Png : codec,
                    MayBecomeJpeg = codec != ImageCodec.Jpeg && canRename,
                });
            }
        }
        catch (DocumentNotSupportedException) { throw; }
        catch (Exception ex) when (ex is FileFormatException or IOException or InvalidDataException or EndOfStreamException)
        {
            throw new DocumentNotSupportedException("hwp 파일이 깨졌거나 다른 형식이에요.");
        }
    }

    /// <returns>문서가 압축돼 있는지 (속성 비트 0)</returns>
    private static bool ReadHeader(RootStorage root)
    {
        if (!root.TryOpenStream("FileHeader", out var header))
            throw new DocumentNotSupportedException("hwp 문서 구조가 아니에요.");
        using (header)
        {
            var buf = new byte[256];
            int read = header.ReadAtLeast(buf, 40, throwOnEndOfStream: false);
            if (read < 40 || !Encoding.ASCII.GetString(buf, 0, Signature.Length).Equals(Signature, StringComparison.Ordinal))
                throw new DocumentNotSupportedException("hwp 문서 구조가 아니에요. (hwp 3.0 이전 문서는 지원하지 않아요)");

            uint properties = BitConverter.ToUInt32(buf, 36);
            if ((properties & ProtectedMask) != 0)
                throw new DocumentNotSupportedException("암호·배포용·전자서명이 있는 문서는 바꾸지 않아요.");
            return (properties & 1u) != 0;
        }
    }

    /// <summary>DocInfo 스트림을 (필요하면 풀어서) 읽는다. 없으면 null.</summary>
    private byte[]? ReadDocInfo(Storage root)
    {
        if (!root.TryOpenStream("DocInfo", out var stream)) return null;
        byte[] raw;
        using (stream)
        {
            raw = new byte[stream.Length];
            stream.ReadExactly(raw);
        }
        if (!_docCompressed) return raw;
        using var input = new DeflateStream(new MemoryStream(raw, writable: false), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// DocInfo의 BIN_DATA 기록에서 그림 번호와 확장자를 읽어, 스트림 이름(BIN0001.bmp)별로
    /// 확장자 글자의 위치를 적어 둔다. 읽지 못하면 형식 바꾸기만 하지 않는다 (그림 줄이기는 그대로).
    /// </summary>
    private void ReadBinDataTable(RootStorage root)
    {
        try
        {
            var info = ReadDocInfo(root);
            if (info is null) return;
            foreach (var (tag, data, size) in Records(info))
            {
                if (tag != TagBinData || size < 6) continue;
                ushort attr = BitConverter.ToUInt16(info, data);
                if ((attr & 0xF) != 1) continue; // EMBEDDING만 BinData 스트림이 있다
                ushort id = BitConverter.ToUInt16(info, data + 2);
                int extLen = BitConverter.ToUInt16(info, data + 4);
                int extOff = data + 6;
                if (extLen is < 1 or > 8 || extOff + extLen * 2 > data + size) continue;
                var ext = Encoding.Unicode.GetString(info, extOff, extLen * 2);
                _extAt[$"BIN{id:X4}.{ext}"] = (extOff, extLen);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException or IOException)
        {
            _extAt.Clear();
        }
    }

    /// <summary>hwp 레코드 목록: (태그, 내용 시작 위치, 내용 크기)</summary>
    private static IEnumerable<(int Tag, int Data, int Size)> Records(byte[] buf)
    {
        int p = 0;
        while (p + 4 <= buf.Length)
        {
            uint h = BitConverter.ToUInt32(buf, p);
            int tag = (int)(h & 0x3FF);
            long size = (h >> 20) & 0xFFF;
            int hdr = 4;
            if (size == 0xFFF)
            {
                if (p + 8 > buf.Length) yield break;
                size = BitConverter.ToUInt32(buf, p + 4);
                hdr = 8;
            }
            if (p + hdr + size > buf.Length) yield break;
            yield return (tag, p + hdr, (int)size);
            p += hdr + (int)size;
        }
    }

    /// <summary>스트림이 그림 그대로인지, deflate로 압축된 그림인지 알아낸다.</summary>
    private static (byte[]? Data, bool Compressed) Unwrap(byte[] raw)
    {
        if (ImageSniffer.Detect(raw) != ImageCodec.Unknown) return (raw, false);
        try
        {
            using var input = new DeflateStream(new MemoryStream(raw, writable: false), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            long total = 0;
            int n;
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += n;
                if (total > MaxInflatedBytes) return (null, false);
                output.Write(buffer, 0, n);
            }
            var data = output.ToArray();
            return ImageSniffer.Detect(data) != ImageCodec.Unknown ? (data, true) : (null, false);
        }
        catch (InvalidDataException)
        {
            return (null, false);
        }
    }

    public byte[] Build(IReadOnlyDictionary<string, RecodedImage> replacements)
    {
        var work = new MemoryStream();
        work.Write(_original);
        work.Position = 0;

        using (var root = RootStorage.Open(work, StorageModeFlags.LeaveOpen))
        {
            if (replacements.Count > 0)
            {
                var bin = root.OpenStorage("BinData");
                byte[]? docInfo = null;
                foreach (var (name, image) in replacements)
                {
                    var source = _images.First(i => i.Key == name);
                    var codec = image.CodecFor(source);
                    var target = name;
                    if (codec != source.Codec)
                    {
                        // 형식이 바뀌었으면 DocInfo의 확장자와 스트림 이름을 함께 바꾼다.
                        var ext = RecodedImage.ExtensionOf(codec);
                        if (!_extAt.TryGetValue(name, out var at) || at.Length != ext.Length) continue;
                        target = name[..(name.LastIndexOf('.') + 1)] + ext;
                        if (!string.Equals(target, name, StringComparison.OrdinalIgnoreCase) && bin.ContainsEntry(target)) continue;
                        docInfo ??= ReadDocInfo(root) ?? throw new IOException("DocInfo를 읽지 못했습니다.");
                        Encoding.Unicode.GetBytes(ext, 0, ext.Length, docInfo, at.Offset);
                    }

                    var bytes = _compressed.TryGetValue(name, out var c) && c ? Deflate(image.Data) : image.Data;
                    if (!string.Equals(target, name, StringComparison.Ordinal))
                    {
                        bin.Delete(name);
                        using var created = bin.CreateStream(target);
                        created.Write(bytes, 0, bytes.Length);
                    }
                    else
                    {
                        using var s = bin.OpenStream(name);
                        s.SetLength(0);
                        s.Write(bytes, 0, bytes.Length);
                    }
                }

                if (docInfo is not null)
                {
                    var stored = _docCompressed ? Deflate(docInfo) : docInfo;
                    using var s = root.OpenStream("DocInfo");
                    s.SetLength(0);
                    s.Write(stored, 0, stored.Length);
                }
            }
            // 빈 공간을 걷어 내야 파일이 실제로 작아진다.
            root.Flush(consolidate: true);
        }
        return work.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var d = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) d.Write(data);
        return ms.ToArray();
    }

    public void Dispose() { }
}
