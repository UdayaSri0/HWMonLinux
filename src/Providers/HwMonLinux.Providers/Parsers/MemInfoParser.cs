using System.Globalization;

namespace HwMonLinux.Providers.Parsers;

internal static class MemInfoParser
{
    public static MemorySnapshot Parse(string content)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var data = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            if (!long.TryParse(parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            data[parts[0]] = value * 1024; // Values are reported as KiB.
        }

        data.TryGetValue("MemTotal", out var total);
        data.TryGetValue("MemAvailable", out var available);

        return new MemorySnapshot(total, available);
    }
}

internal readonly record struct MemorySnapshot(long TotalBytes, long AvailableBytes)
{
    public long UsedBytes => Math.Max(0, TotalBytes - AvailableBytes);

    public double UsagePercentage => TotalBytes <= 0
        ? 0
        : UsedBytes / (double)TotalBytes * 100d;
}
