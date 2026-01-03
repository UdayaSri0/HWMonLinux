using System.Globalization;
using HwMonLinux.Core;

namespace HwMonLinux.Providers.Providers;

public sealed class SysfsHwmonProvider : ISensorProvider
{
    private readonly string _rootPath;

    public SysfsHwmonProvider(string? rootPath = null)
    {
        _rootPath = rootPath ?? "/sys/class/hwmon";
    }

    public string Name => "sysfs hwmon";

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        new(Directory.Exists(_rootPath));

    public async Task<IReadOnlyCollection<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken)
    {
        var sensors = new List<SensorReading>();
        if (!Directory.Exists(_rootPath))
        {
            return sensors;
        }

        foreach (var hwmonDirectory in Directory.EnumerateDirectories(_rootPath, "hwmon*"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var deviceName = (await ReadTrimmedAsync(Path.Combine(hwmonDirectory, "name"), cancellationToken))
                ?? Path.GetFileName(hwmonDirectory);

            sensors.AddRange(await ReadTemperatureSensorsAsync(hwmonDirectory, deviceName, cancellationToken).ConfigureAwait(false));
            sensors.AddRange(await ReadFanSensorsAsync(hwmonDirectory, deviceName, cancellationToken).ConfigureAwait(false));
            sensors.AddRange(await ReadVoltageSensorsAsync(hwmonDirectory, deviceName, cancellationToken).ConfigureAwait(false));
        }

        return sensors;
    }

    private async Task<IEnumerable<SensorReading>> ReadTemperatureSensorsAsync(string directory, string deviceName, CancellationToken cancellationToken)
    {
        var list = new List<SensorReading>();
        foreach (var tempFile in Directory.EnumerateFiles(directory, "temp*_input"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baseName = Path.GetFileName(tempFile).Replace("_input", string.Empty, StringComparison.Ordinal);
            var labelPath = Path.Combine(directory, $"{baseName}_label");

            var label = await ReadTrimmedAsync(labelPath, cancellationToken).ConfigureAwait(false);
            var rawValue = await ReadTrimmedAsync(tempFile, cancellationToken).ConfigureAwait(false);
            if (!TryParseMilliValue(rawValue, out var value))
            {
                continue;
            }

            var lowerLabel = $"{label} {deviceName}".ToLowerInvariant();
            var sensorType = GuessTemperatureType(lowerLabel);
            var friendlyName = label ?? $"{deviceName} {baseName}";
            var sensorId = $"temp.{deviceName}.{baseName}".ToLowerInvariant();

            list.Add(SensorFactory.Create(
                sensorId,
                GroupPath.From("Sensors", deviceName, "Temperatures"),
                friendlyName,
                sensorType,
                value,
                "C",
                Name));
        }

        return list;
    }

    private async Task<IEnumerable<SensorReading>> ReadFanSensorsAsync(string directory, string deviceName, CancellationToken cancellationToken)
    {
        var list = new List<SensorReading>();
        foreach (var fanFile in Directory.EnumerateFiles(directory, "fan*_input"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var baseName = Path.GetFileName(fanFile).Replace("_input", string.Empty, StringComparison.Ordinal);
            var label = await ReadTrimmedAsync(Path.Combine(directory, $"{baseName}_label"), cancellationToken).ConfigureAwait(false);
            var rawValue = await ReadTrimmedAsync(fanFile, cancellationToken).ConfigureAwait(false);
            if (!double.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var rpm))
            {
                continue;
            }

            var friendlyName = label ?? $"{deviceName} {baseName}";
            var sensorId = $"fan.{deviceName}.{baseName}".ToLowerInvariant();
            list.Add(SensorFactory.Create(
                sensorId,
                GroupPath.From("Sensors", deviceName, "Fans"),
                friendlyName,
                SensorType.FanSpeed,
                rpm,
                "RPM",
                Name));
        }

        return list;
    }

    private async Task<IEnumerable<SensorReading>> ReadVoltageSensorsAsync(string directory, string deviceName, CancellationToken cancellationToken)
    {
        var list = new List<SensorReading>();
        foreach (var voltageFile in Directory.EnumerateFiles(directory, "in*_input"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var baseName = Path.GetFileName(voltageFile).Replace("_input", string.Empty, StringComparison.Ordinal);
            var label = await ReadTrimmedAsync(Path.Combine(directory, $"{baseName}_label"), cancellationToken).ConfigureAwait(false);
            var rawValue = await ReadTrimmedAsync(voltageFile, cancellationToken).ConfigureAwait(false);
            if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var volts))
            {
                continue;
            }

            // Most hwmon voltage values are reported in millivolts.
            if (volts > 100)
            {
                volts /= 1000d;
            }

            var friendlyName = label ?? $"{deviceName} {baseName}";
            var sensorId = $"volt.{deviceName}.{baseName}".ToLowerInvariant();
            list.Add(SensorFactory.Create(
                sensorId,
                GroupPath.From("Sensors", deviceName, "Voltages"),
                friendlyName,
                SensorType.Voltage,
                volts,
                "V",
                Name));
        }

        return list;
    }

    private static SensorType GuessTemperatureType(string label)
    {
        if (label.Contains("gpu", StringComparison.OrdinalIgnoreCase))
        {
            return SensorType.GpuTemperature;
        }

        if (label.Contains("cpu", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("core", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("package", StringComparison.OrdinalIgnoreCase))
        {
            return SensorType.CpuTemperature;
        }

        return SensorType.Unknown;
    }

    private static bool TryParseMilliValue(string? rawValue, out double parsed)
    {
        parsed = 0;
        if (!long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var millivalue))
        {
            return false;
        }

        parsed = millivalue / 1000d;
        return true;
    }

    private static async Task<string?> ReadTrimmedAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return content.Trim();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
