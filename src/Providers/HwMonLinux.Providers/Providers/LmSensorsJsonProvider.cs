using System;
using System.Collections.Generic;
using System.Text.Json;
using HwMonLinux.Core;
using HwMonLinux.Providers.Abstractions;

namespace HwMonLinux.Providers.Providers;

public sealed class LmSensorsJsonProvider : ISensorProvider
{
    private readonly IProcessRunner _processRunner;

    public LmSensorsJsonProvider(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string Name => "lm-sensors";

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        _processRunner.IsAvailableAsync("sensors", cancellationToken);

    public async Task<IReadOnlyCollection<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync("sensors", "-j", cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return Array.Empty<SensorReading>();
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var sensors = new List<SensorReading>();
            foreach (var chip in document.RootElement.EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();
                sensors.AddRange(ParseChip(chip));
            }

            return sensors;
        }
        catch (JsonException)
        {
            return Array.Empty<SensorReading>();
        }
    }

    private IEnumerable<SensorReading> ParseChip(JsonProperty chipProperty)
    {
        var chipName = chipProperty.Name;
        var chipObject = chipProperty.Value;
        foreach (var sensorSection in chipObject.EnumerateObject())
        {
            if (sensorSection.NameEquals("Adapter") || sensorSection.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var measurement in ExtractMeasurements(chipName, sensorSection.Name, sensorSection.Value))
            {
                yield return measurement;
            }
        }
    }

    private IEnumerable<SensorReading> ExtractMeasurements(string chipName, string sectionName, JsonElement section)
    {
        var labelLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in section.EnumerateObject())
        {
            if (property.Name.EndsWith("_label", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                labelLookup[property.Name[..^6]] = property.Value.GetString()!;
            }
        }

        foreach (var property in section.EnumerateObject())
        {
            if (!property.Name.EndsWith("_input", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var prefix = property.Name.Split('_')[0];
            var labelKey = property.Name[..^6];
            labelLookup.TryGetValue(labelKey, out var label);
            var friendlyName = label ?? $"{chipName} {property.Name}";
            var groupLabel = DetermineGroupLabel(prefix);
            var sensorType = DetermineSensorType(prefix, friendlyName);
            var unit = DetermineUnit(prefix);
            var value = property.Value.GetDouble();
            if (prefix.Equals("temp", StringComparison.OrdinalIgnoreCase))
            {
                friendlyName = label ?? $"{chipName} {sectionName}";
            }

            var groupPath = BuildGroupPath(chipName, sensorType, groupLabel, friendlyName);
            yield return SensorFactory.Create(
                $"{chipName}.{property.Name}".ToLowerInvariant(),
                groupPath,
                friendlyName,
                sensorType,
                value,
                unit,
                Name);
        }
    }

    private static string DetermineGroupLabel(string prefix) => prefix.ToLowerInvariant() switch
    {
        "temp" => "Temperatures",
        "fan" => "Fans",
        "power" => "Power",
        "in" => "Voltages",
        "curr" => "Currents",
        _ => "Readings"
    };

    private static SensorType DetermineSensorType(string prefix, string friendlyName)
    {
        return prefix.ToLowerInvariant() switch
        {
            "temp" => GuessTemperatureType(friendlyName),
            "fan" => SensorType.FanSpeed,
            "power" => SensorType.Power,
            "in" => SensorType.Voltage,
            "curr" => SensorType.Current,
            _ => SensorType.Unknown
        };
    }

    private static SensorType GuessTemperatureType(string friendlyName)
    {
        var lower = friendlyName.ToLowerInvariant();
        if (lower.Contains("gpu"))
        {
            return SensorType.GpuTemperature;
        }

        if (lower.Contains("cpu") || lower.Contains("core") || lower.Contains("package"))
        {
            return SensorType.CpuTemperature;
        }

        return SensorType.Unknown;
    }

    private static string DetermineUnit(string prefix) => prefix.ToLowerInvariant() switch
    {
        "temp" => "C",
        "fan" => "RPM",
        "power" => "W",
        "in" => "V",
        "curr" => "A",
        _ => string.Empty
    };

    private static IReadOnlyList<string> BuildGroupPath(string chipName, SensorType type, string groupLabel, string? friendlyName)
    {
        var root = GuessRoot(chipName, friendlyName, type);
        if (root.Equals("Sensors", StringComparison.OrdinalIgnoreCase))
        {
            return GroupPath.From(root, chipName, groupLabel);
        }

        return GroupPath.From(root, groupLabel);
    }

    private static string GuessRoot(string chipName, string? friendlyName, SensorType type)
    {
        var lowerChip = chipName.ToLowerInvariant();
        var lowerName = friendlyName?.ToLowerInvariant();

        if (lowerChip.Contains("coretemp", StringComparison.OrdinalIgnoreCase) ||
            lowerName?.Contains("cpu", StringComparison.OrdinalIgnoreCase) == true ||
            lowerName?.Contains("package", StringComparison.OrdinalIgnoreCase) == true ||
            type == SensorType.CpuTemperature ||
            (type == SensorType.Power && lowerName?.Contains("cpu", StringComparison.OrdinalIgnoreCase) == true))
        {
            return "CPU";
        }

        if (lowerChip.Contains("gpu", StringComparison.OrdinalIgnoreCase) ||
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

        if (lowerChip.Contains("nvme", StringComparison.OrdinalIgnoreCase))
        {
            return "Disks";
        }

        if (type == SensorType.FanSpeed)
        {
            return "Fans";
        }

        return "Sensors";
    }
}
