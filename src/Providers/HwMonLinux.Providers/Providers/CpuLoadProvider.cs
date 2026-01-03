using HwMonLinux.Core;
using HwMonLinux.Providers.Parsers;

namespace HwMonLinux.Providers.Providers;

public sealed class CpuLoadProvider : ISensorProvider
{
    private readonly string _procStatPath;
    private IReadOnlyDictionary<string, CpuTimes>? _previousSnapshot;

    public CpuLoadProvider(string? procStatPath = null)
    {
        _procStatPath = procStatPath ?? "/proc/stat";
    }

    public string Name => "/proc/stat";

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        new(File.Exists(_procStatPath));

    public async Task<IReadOnlyCollection<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_procStatPath))
        {
            return Array.Empty<SensorReading>();
        }

        var content = await File.ReadAllTextAsync(_procStatPath, cancellationToken).ConfigureAwait(false);
        var snapshot = ProcStatParser.Parse(content);

        if (_previousSnapshot is null || _previousSnapshot.Count == 0)
        {
            _previousSnapshot = snapshot;
            return Array.Empty<SensorReading>();
        }

        var readings = new List<SensorReading>();
        foreach (var pair in snapshot)
        {
            if (!_previousSnapshot.TryGetValue(pair.Key, out var previous))
            {
                continue;
            }

            var usage = pair.Value.CalculateUsage(previous);
            var sensorId = $"cpu.load.{pair.Key.ToLowerInvariant()}";
            var friendlyName = pair.Key.Equals("cpu", StringComparison.OrdinalIgnoreCase)
                ? "CPU Load"
                : $"{pair.Key.ToUpperInvariant()} Load";

            var group = pair.Key.Equals("cpu", StringComparison.OrdinalIgnoreCase)
                ? GroupPath.From("CPU", "Load", "Total")
                : GroupPath.From("CPU", "Load", pair.Key.ToUpperInvariant());

            readings.Add(SensorFactory.Create(
                sensorId,
                group,
                friendlyName,
                SensorType.CpuLoad,
                usage,
                "%",
                Name));
        }

        _previousSnapshot = snapshot;
        return readings;
    }
}
