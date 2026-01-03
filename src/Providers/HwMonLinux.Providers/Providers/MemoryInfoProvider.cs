using HwMonLinux.Core;
using HwMonLinux.Providers.Parsers;

namespace HwMonLinux.Providers.Providers;

public sealed class MemoryInfoProvider : ISensorProvider
{
    private readonly string _memInfoPath;

    public MemoryInfoProvider(string? memInfoPath = null)
    {
        _memInfoPath = memInfoPath ?? "/proc/meminfo";
    }

    public string Name => "/proc/meminfo";

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        new(File.Exists(_memInfoPath));

    public async Task<IReadOnlyCollection<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_memInfoPath))
        {
            return Array.Empty<SensorReading>();
        }

        var content = await File.ReadAllTextAsync(_memInfoPath, cancellationToken).ConfigureAwait(false);
        var snapshot = MemInfoParser.Parse(content);
        if (snapshot.TotalBytes <= 0)
        {
            return Array.Empty<SensorReading>();
        }

        var usedGiB = BytesToGiB(snapshot.UsedBytes);
        var totalGiB = BytesToGiB(snapshot.TotalBytes);
        var availableGiB = BytesToGiB(snapshot.AvailableBytes);

        var readings = new List<SensorReading>
        {
            new("memory.usage.percent", "RAM Usage", SensorType.MemoryUsage, snapshot.UsagePercentage, "%", Name,
                Description: $"{usedGiB:F1} / {totalGiB:F1} GiB"),
            new("memory.available", "Available RAM", SensorType.MemoryAvailable, availableGiB, "GiB", Name)
        };

        return readings;
    }

    private static double BytesToGiB(long bytes) =>
        bytes / 1024d / 1024d / 1024d;
}
