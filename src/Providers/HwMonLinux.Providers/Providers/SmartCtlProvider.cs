using HwMonLinux.Core;
using HwMonLinux.Providers.Abstractions;
using HwMonLinux.Providers.Parsers;

namespace HwMonLinux.Providers.Providers;

public sealed class SmartCtlProvider : ISensorProvider
{
    private readonly IProcessRunner _processRunner;
    private readonly string _blockPath;

    public SmartCtlProvider(IProcessRunner processRunner, string? blockPath = null)
    {
        _processRunner = processRunner;
        _blockPath = blockPath ?? "/sys/block";
    }

    public string Name => "smartctl";

    public async ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_blockPath))
        {
            return false;
        }

        return await _processRunner.IsAvailableAsync("smartctl", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken)
    {
        var sensors = new List<SensorReading>();

        foreach (var device in EnumerateDevices())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _processRunner.RunAsync("smartctl", $"-j /dev/{device}", cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                continue;
            }

            var status = SmartCtlParser.Parse(result.StandardOutput);
            if (status is null)
            {
                continue;
            }

            var diskName = status.DeviceIdentifier.Replace(' ', '_');
            var friendlyName = status.Model ?? status.DeviceIdentifier;

            if (status.TemperatureCelsius is double temperature)
            {
                sensors.Add(new SensorReading(
                    $"disk.{diskName}.temp",
                    $"{friendlyName} Temp",
                    SensorType.DiskTemperature,
                    temperature,
                    "C",
                    Name));
            }

            if (status.IsHealthy is bool healthy)
            {
                sensors.Add(new SensorReading(
                    $"disk.{diskName}.health",
                    $"{friendlyName} SMART",
                    SensorType.DiskHealth,
                    healthy ? 1 : 0,
                    string.Empty,
                    Name,
                    TextValue: healthy ? "OK" : "Attention"));
            }
        }

        return sensors;
    }

    private IEnumerable<string> EnumerateDevices()
    {
        if (!Directory.Exists(_blockPath))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFileSystemEntries(_blockPath))
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (name.StartsWith("loop", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("ram", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.StartsWith("nvme", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("sd", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("vd", StringComparison.OrdinalIgnoreCase))
            {
                yield return name;
            }
        }
    }
}
