using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using HwMonLinux.Core;

namespace HwMonLinux.Providers.Providers;

public sealed partial class GpuSysfsProvider : ISensorProvider
{
    private readonly string _drmRoot;

    public GpuSysfsProvider(string? drmRoot = null)
    {
        _drmRoot = drmRoot ?? "/sys/class/drm";
    }

    public string Name => "GPU sysfs";

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        new(Directory.Exists(_drmRoot));

    public async Task<IReadOnlyCollection<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_drmRoot))
        {
            return Array.Empty<SensorReading>();
        }

        var sensors = new List<SensorReading>();
        foreach (var cardDirectory in Directory.EnumerateDirectories(_drmRoot, "card*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CardRegex().IsMatch(Path.GetFileName(cardDirectory)))
            {
                continue;
            }

            var name = Path.GetFileName(cardDirectory).ToUpperInvariant();
            var vendor = await ReadTrimmedAsync(Path.Combine(cardDirectory, "device", "vendor"), cancellationToken).ConfigureAwait(false);
            var vendorName = vendor switch
            {
                "0x1002" => "AMD",
                "0x8086" => "Intel",
                _ => "GPU"
            };

            var hwmonRoot = Path.Combine(cardDirectory, "device", "hwmon");
            if (!Directory.Exists(hwmonRoot))
            {
                continue;
            }

            foreach (var hwmon in Directory.EnumerateDirectories(hwmonRoot, "hwmon*"))
            {
                sensors.AddRange(await ReadTemperaturesAsync(hwmon, vendorName, name, cancellationToken).ConfigureAwait(false));
                sensors.AddRange(await ReadPowerAsync(hwmon, vendorName, name, cancellationToken).ConfigureAwait(false));
            }
        }

        return sensors;
    }

    private async Task<IEnumerable<SensorReading>> ReadTemperaturesAsync(string hwmonDirectory, string vendorName, string cardName, CancellationToken cancellationToken)
    {
        var list = new List<SensorReading>();
        foreach (var tempFile in Directory.EnumerateFiles(hwmonDirectory, "temp*_input"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var labelFile = Path.Combine(hwmonDirectory, Path.GetFileName(tempFile).Replace("_input", "_label", StringComparison.Ordinal));
            var label = await ReadTrimmedAsync(labelFile, cancellationToken).ConfigureAwait(false);
            var rawValue = await ReadTrimmedAsync(tempFile, cancellationToken).ConfigureAwait(false);
            if (!long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var millidegree))
            {
                continue;
            }

            var sensorId = $"gpu.sysfs.{cardName}.{Path.GetFileName(tempFile)}".ToLowerInvariant();
            var friendly = label ?? $"{cardName} Temp";

            list.Add(SensorFactory.Create(
                sensorId,
                GroupPath.From("GPU", $"{vendorName} {cardName}", "Temperatures"),
                friendly,
                SensorType.GpuTemperature,
                millidegree / 1000d,
                "C",
                Name));
        }

        return list;
    }

    private async Task<IEnumerable<SensorReading>> ReadPowerAsync(string hwmonDirectory, string vendorName, string cardName, CancellationToken cancellationToken)
    {
        var list = new List<SensorReading>();
        foreach (var powerFile in Directory.EnumerateFiles(hwmonDirectory, "power*_input"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baseName = Path.GetFileName(powerFile).Replace("_input", string.Empty, StringComparison.Ordinal);
            var labelFile = Path.Combine(hwmonDirectory, $"{baseName}_label");
            var label = await ReadTrimmedAsync(labelFile, cancellationToken).ConfigureAwait(false);
            var rawValue = await ReadTrimmedAsync(powerFile, cancellationToken).ConfigureAwait(false);
            if (!TryParseMicrowatts(rawValue, out var watts))
            {
                continue;
            }

            var sensorId = $"gpu.sysfs.{cardName}.{baseName}".ToLowerInvariant();
            var friendly = label ?? $"{cardName} {baseName}";
            list.Add(SensorFactory.Create(
                sensorId,
                GroupPath.From("GPU", $"{vendorName} {cardName}", "Powers"),
                friendly,
                SensorType.Power,
                watts,
                "W",
                Name));
        }

        return list;
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
        catch
        {
            return null;
        }
    }

    private static bool TryParseMicrowatts(string? rawValue, out double watts)
    {
        watts = 0;
        if (!long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microwatts))
        {
            return false;
        }

        if (microwatts > 1_000_000)
        {
            watts = microwatts / 1_000_000d;
        }
        else if (microwatts > 1000)
        {
            watts = microwatts / 1000d;
        }
        else
        {
            watts = microwatts;
        }

        return true;
    }

    [GeneratedRegex("^card\\d+$", RegexOptions.Compiled)]
    private static partial Regex CardRegex();
}
