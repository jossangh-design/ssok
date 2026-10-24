using System.IO.Compression;

namespace Ssok.Core.Documents;

/// <summary>JPEG 앞에 덧씌워진 단순 PDF 필터(ASCII85, ASCIIHex, Flate)를 푼다. 모르는 필터면 null.</summary>
internal static class PdfFilters
{
    private const long MaxDecoded = 300L * 1024 * 1024;

    public static byte[]? Decode(byte[] data, IEnumerable<string> filters)
    {
        byte[]? current = data;
        foreach (var f in filters)
        {
            current = f switch
            {
                "/ASCII85Decode" or "/A85" => Ascii85(current),
                "/ASCIIHexDecode" or "/AHx" => AsciiHex(current),
                "/FlateDecode" or "/Fl" => Flate(current),
                _ => null,
            };
            if (current is null) return null;
        }
        return current;
    }

    private static byte[]? Ascii85(byte[] input)
    {
        var output = new List<byte>(input.Length * 4 / 5 + 4);
        uint value = 0;
        int count = 0;
        int i = 0;
        if (input.Length >= 2 && input[0] == '<' && input[1] == '~') i = 2;
        for (; i < input.Length; i++)
        {
            byte c = input[i];
            if (c == '~') break; // 끝 표시 "~>"
            if (c is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or (byte)'\f' or 0) continue;
            if (c == 'z' && count == 0) { output.AddRange(new byte[4]); continue; }
            if (c < '!' || c > 'u') return null;
            value = value * 85 + (uint)(c - '!');
            if (++count == 5)
            {
                output.Add((byte)(value >> 24)); output.Add((byte)(value >> 16)); output.Add((byte)(value >> 8)); output.Add((byte)value);
                value = 0; count = 0;
            }
            if (output.Count > MaxDecoded) return null;
        }
        if (count > 0)
        {
            if (count == 1) return null;
            for (int k = count; k < 5; k++) value = value * 85 + 84;
            for (int k = 0; k < count - 1; k++) output.Add((byte)(value >> (24 - 8 * k)));
        }
        return output.ToArray();
    }

    private static byte[]? AsciiHex(byte[] input)
    {
        var output = new List<byte>(input.Length / 2 + 1);
        int hi = -1;
        foreach (byte c in input)
        {
            if (c == '>') break;
            int v = c switch
            {
                >= (byte)'0' and <= (byte)'9' => c - '0',
                >= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
                >= (byte)'A' and <= (byte)'F' => c - 'A' + 10,
                _ => -1,
            };
            if (v < 0) { if (char.IsWhiteSpace((char)c)) continue; return null; }
            if (hi < 0) hi = v; else { output.Add((byte)(hi * 16 + v)); hi = -1; }
        }
        if (hi >= 0) output.Add((byte)(hi * 16));
        return output.ToArray();
    }

    private static byte[]? Flate(byte[] input)
    {
        try
        {
            using var z = new ZLibStream(new MemoryStream(input, writable: false), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            var buf = new byte[81920];
            int n; long total = 0;
            while ((n = z.Read(buf, 0, buf.Length)) > 0)
            {
                total += n;
                if (total > MaxDecoded) return null;
                ms.Write(buf, 0, n);
            }
            return ms.ToArray();
        }
        catch (InvalidDataException) { return null; }
    }
}
