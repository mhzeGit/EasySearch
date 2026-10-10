using System.Globalization;

namespace EasySearch;

/// <summary>Bound each media response to 1 MiB, including open-ended browser range requests.</summary>
internal readonly record struct VideoByteRange(long Start, int Count)
{
    internal const int MaxBytes = 1024 * 1024;
    public long End => Start + Count - 1;

    public static VideoByteRange? Parse(string? header, long length)
    {
        if (length <= 0) return null;
        long start = 0, end = length - 1;
        if (!string.IsNullOrEmpty(header))
        {
            if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return null;
            var parts = header[6..].Split('-');
            if (parts.Length != 2) return null;
            bool Number(string value, out long number) =>
                long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
            if (parts[0].Length == 0)
            {
                if (!Number(parts[1], out long suffix) || suffix <= 0) return null;
                start = Math.Max(0, length - suffix);
            }
            else
            {
                if (!Number(parts[0], out start) || start >= length) return null;
                if (parts[1].Length != 0)
                {
                    if (!Number(parts[1], out end) || end < start) return null;
                    end = Math.Min(end, length - 1);
                }
            }
        }
        return new(start, (int)Math.Min(end - start + 1, MaxBytes));
    }
}
