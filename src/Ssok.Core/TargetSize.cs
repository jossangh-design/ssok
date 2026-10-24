using System.Globalization;
using System.Text.RegularExpressions;

namespace Ssok.Core;

/// <summary>목표 용량. "300KB", "1.5MB", "800" (KB로 간주) 같은 입력을 바이트로 바꾼다.</summary>
public static partial class TargetSize
{
    public const long MinBytes = 10 * 1024;          // 10KB 미만은 의미 없는 결과가 나오므로 막는다.
    public const long MaxBytes = 2L * 1024 * 1024 * 1024;

    [GeneratedRegex(@"^\s*(?<n>\d+(?:[.,]\d+)?)\s*(?<u>kb|k|mb|m|gb|g|b)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public static bool TryParse(string? text, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = Pattern().Match(text);
        if (!m.Success) return false;

        var number = double.Parse(m.Groups["n"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var unit = m.Groups["u"].Success ? m.Groups["u"].Value.ToLowerInvariant() : "kb";
        double multiplier = unit switch
        {
            "b" => 1,
            "kb" or "k" => 1024,
            "mb" or "m" => 1024 * 1024,
            "gb" or "g" => 1024d * 1024 * 1024,
            _ => 0,
        };

        var value = number * multiplier;
        if (value < MinBytes || value > MaxBytes) return false;
        bytes = (long)Math.Floor(value);
        return true;
    }

    public static string Format(long bytes)
    {
        if (bytes >= 1024 * 1024)
            return (bytes / (1024d * 1024)).ToString("0.##", CultureInfo.InvariantCulture) + "MB";
        return Math.Ceiling(bytes / 1024d).ToString("0", CultureInfo.InvariantCulture) + "KB";
    }
}
