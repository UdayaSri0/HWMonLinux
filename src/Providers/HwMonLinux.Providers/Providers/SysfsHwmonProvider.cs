using System;
using System.Collections.Generic;
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
            sensors.AddRange(await ReadPowerSensorsAsync(hwmonDirectory, deviceName, cancellationToken).ConfigureAwait(false));
            sensors.AddRange(await ReadPwmSensorsAsync(hwmonDirectory, deviceName, cancellationToken).ConfigureAwait(false));
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
            var groupPath = BuildGroupPath(deviceName, "Temperatures", sensorType, friendlyName);

            list.Add(SensorFactory.Create(
                sensorId,
                groupPath,
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
            var groupPath = BuildGroupPath(deviceName, "Fans", SensorType.FanSpeed, friendlyName);
            list.Add(SensorFactory.Create(
                sensorId,
                groupPath,
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
            if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var voltsRaw))
            {
                continue;
            }

            var volts = NormalizeScaledValue(voltsRaw);

            var friendlyName = label ?? $"{deviceName} {baseName}";
            var sensorId = $"volt.{deviceName}.{baseName}".ToLowerInvariant();
            var groupPath = BuildGroupPath(deviceName, "Voltages", SensorType.Voltage, friendlyName);
            list.Add(SensorFactory.Create(
                sensorId,
                groupPath,
                friendlyName,
                SensorType.Voltage,
                volts,
                "V",
                Name));
        }

        return list;
    }

    private static double NormalizeScaledValue(double raw)
    {
        if (raw > 1_000_000)
        {
            return raw / 1_000_000d;
        }

        if (raw > 1000)
        {
            return raw / 1000d;
        }

        return raw;
    }

    private async Task<IEnumerable<SensorReading>> ReadPowerSensorsAsync(string directory, string deviceName, CancellationToken cancellationToken)
    {
        var list = new List<SensorReading>();
        foreach (var powerFile in Directory.EnumerateFiles(directory, "power*_input"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var baseName = Path.GetFileName(powerFile).Replace("_input", string.Empty, StringComparison.Ordinal);
            var label = await ReadTrimmedAsync(Path.Combine(directory, $"{baseName}_label"), cancellationToken).ConfigureAwait(false);
            var rawValue = await ReadTrimmedAsync(powerFile, cancellationToken).ConfigureAwait(false);
            if (!TryParseMicrowattValue(rawValue, out var watts))
            {
                continue;
            }

            var friendlyName = label ?? $"{deviceName} {baseName}";
            var sensorId = $"power.{deviceName}.{baseName}".ToLowerInvariant();
            var groupPath = BuildGroupPath(deviceName, "Powers", SensorType.Power, friendlyName);

            list.Add(SensorFactory.Create(
                sensorId,
                groupPath,
                friendlyName,
                SensorType.Power,
                watts,
                "W",
                Name));
        }

        return list;
    }

    private async Task<IEnumerable<SensorReading>> ReadPwmSensorsAsync(string directory, string deviceName, CancellationToken cancellationToken)
    {
        var list = new List<SensorReading>();

        foreach (var enableFile in Directory.EnumerateFiles(directory, "pwm*_enable"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = await ReadTrimmedAsync(enableFile, cancellationToken).ConfigureAwait(false);
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode))
            {
                continue;
            }

            var baseName = Path.GetFileName(enableFile).Replace("_enable", string.Empty, StringComparison.Ordinal);
            var friendlyName = $"{deviceName} {baseName} mode";
            var sensorId = $"pwm.{deviceName}.{baseName}.mode".ToLowerInvariant();
            var groupPath = BuildGroupPath(deviceName, "Control", SensorType.FanSpeed, friendlyName);
            var metadata = new Dictionary<string, string> { { "typeDisplay", "Fan Control" } };

            list.Add(SensorFactory.Create(
                sensorId,
                groupPath,
                friendlyName,
                SensorType.Unknown,
                mode,
                string.Empty,
                Name,
                textValue: DescribePwmMode(mode),
                metadata: metadata));
        }

        foreach (var pwmFile in Directory.EnumerateFiles(directory, "pwm[0-9]*"))
        {
            if (pwmFile.EndsWith("_enable", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var raw = await ReadTrimmedAsync(pwmFile, cancellationToken).ConfigureAwait(false);
            if (!double.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var pwmValue))
            {
                continue;
            }

            var percent = Math.Clamp(pwmValue / 255d * 100d, 0d, 100d);
            var baseName = Path.GetFileName(pwmFile);
            var friendlyName = $"{deviceName} {baseName}";
            var sensorId = $"pwm.{deviceName}.{baseName}".ToLowerInvariant();
            var groupPath = BuildGroupPath(deviceName, "Control", SensorType.FanSpeed, friendlyName);
            var metadata = new Dictionary<string, string> { { "typeDisplay", "Fan Control" } };

            list.Add(SensorFactory.Create(
                sensorId,
                groupPath,
                friendlyName,
                SensorType.Unknown,
                percent,
                "%",
                Name,
                description: $"Raw {pwmValue:F0}/255",
                metadata: metadata));
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

    private static IReadOnlyList<string> BuildGroupPath(string deviceName, string groupLabel, SensorType type, string? friendlyName = null)
    {
        var root = GuessRoot(deviceName, friendlyName, type);
        if (root.Equals("Sensors", StringComparison.OrdinalIgnoreCase))
        {
            return GroupPath.From(root, deviceName, groupLabel);
        }

        return GroupPath.From(root, groupLabel);
    }

    private static string GuessRoot(string deviceName, string? friendlyName, SensorType type)
    {
        var lowerDevice = deviceName.ToLowerInvariant();
        var lowerName = friendlyName?.ToLowerInvariant();

        if (lowerDevice.Contains("coretemp", StringComparison.OrdinalIgnoreCase) ||
            lowerName?.Contains("cpu", StringComparison.OrdinalIgnoreCase) == true ||
            lowerName?.Contains("package", StringComparison.OrdinalIgnoreCase) == true ||
            type == SensorType.CpuTemperature ||
            (type == SensorType.Power && (lowerName?.Contains("cpu", StringComparison.OrdinalIgnoreCase) == true ||
                                          lowerName?.Contains("package", StringComparison.OrdinalIgnoreCase) == true)))
        {
            return "CPU";
        }

        if (lowerDevice.Contains("gpu", StringComparison.OrdinalIgnoreCase) ||
            lowerName?.Contains("gpu", StringComparison.OrdinalIgnoreCase) == true ||
            type == SensorType.GpuTemperature ||
            (type == SensorType.Power && lowerName?.Contains("gpu", StringComparison.OrdinalIgnoreCase) == true))
        {
            return "GPU";
        }

        if (lowerName?.Contains("dimm", StringComparison.OrdinalIgnoreCase) == true ||
            lowerName?.Contains("sodimm", StringComparison.OrdinalIgnoreCase) == true ||
            lowerName?.Contains("mem", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Memory";
        }

        if (type == SensorType.FanSpeed)
        {
            return "Fans";
        }

        if (lowerDevice.Contains("nvme", StringComparison.OrdinalIgnoreCase))
        {
            return "Disks";
        }

        return "Sensors";
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

    private static bool TryParseMicrowattValue(string? rawValue, out double parsed)
    {
        parsed = 0;
        if (!long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microwatts))
        {
            return false;
        }

        if (microwatts > 1_000_000)
        {
            parsed = microwatts / 1_000_000d;
        }
        else if (microwatts > 1000)
        {
            parsed = microwatts / 1000d;
        }
        else
        {
            parsed = microwatts;
        }

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

    private static string DescribePwmMode(int mode) => mode switch
    {
        0 => "Disabled",
        1 => "Manual",
        2 => "Automatic",
        _ => $"Mode {mode}"
    };
}
