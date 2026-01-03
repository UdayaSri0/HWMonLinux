using System.Globalization;

namespace HwMonLinux.Providers.Parsers;

internal static class ProcStatParser
{
    private static readonly char[] Separators = { ' ' };

    public static IReadOnlyDictionary<string, CpuTimes> Parse(string content)
    {
        var result = new Dictionary<string, CpuTimes>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(content))
        {
            return result;
        }

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.StartsWith("cpu", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 8)
            {
                continue;
            }

            var cpuId = parts[0];
            if (TryParseParts(parts, out var sample))
            {
                result[cpuId] = sample;
            }
        }

        return result;
    }

    private static bool TryParseParts(string[] parts, out CpuTimes sample)
    {
        var numbers = new List<ulong>(parts.Length - 1);
        for (var i = 1; i < parts.Length; i++)
        {
            if (ulong.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                numbers.Add(value);
            }
        }

        while (numbers.Count < 8)
        {
            numbers.Add(0);
        }

        sample = new CpuTimes(
            numbers[0],
            numbers[1],
            numbers[2],
            numbers[3],
            numbers[4],
            numbers[5],
            numbers[6],
            numbers[7]);
        return true;
    }
}

internal readonly record struct CpuTimes(
    ulong User,
    ulong Nice,
    ulong System,
    ulong Idle,
    ulong Iowait,
    ulong Irq,
    ulong SoftIrq,
    ulong Steal)
{
    public double CalculateUsage(CpuTimes previous)
    {
        var prevIdle = previous.Idle + previous.Iowait;
        var idle = Idle + Iowait;

        var prevNonIdle = previous.User + previous.Nice + previous.System + previous.Irq + previous.SoftIrq + previous.Steal;
        var nonIdle = User + Nice + System + Irq + SoftIrq + Steal;

        var prevTotal = prevIdle + prevNonIdle;
        var total = idle + nonIdle;

        var totalDelta = (double)(total - prevTotal);
        if (totalDelta <= double.Epsilon)
        {
            return 0.0;
        }

        var idleDelta = (double)(idle - prevIdle);
        var usage = (totalDelta - idleDelta) / totalDelta * 100d;
        return Math.Clamp(usage, 0d, 100d);
    }
}
