using System;
using System.Collections.Generic;
using System.Globalization;
using HwMonLinux.Core;

namespace HwMonLinux.Providers.Providers;

public sealed class BatteryProvider : ISensorProvider
{
    private readonly string _powerSupplyRoot;

    public BatteryProvider(string? powerSupplyRoot = null)
    {
        _powerSupplyRoot = powerSupplyRoot ?? "/sys/class/power_supply";
    }

    public string Name => "power_supply";

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        new(Directory.Exists(_powerSupplyRoot));

    public async Task<IReadOnlyCollection<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_powerSupplyRoot))
        {
            return Array.Empty<SensorReading>();
        }

        var sensors = new List<SensorReading>();
        foreach (var supplyDir in Directory.EnumerateDirectories(_powerSupplyRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(supplyDir);
            var type = await ReadTrimmedAsync(Path.Combine(supplyDir, "type"), cancellationToken).ConfigureAwait(false);

            if (name.StartsWith("BAT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Battery", StringComparison.OrdinalIgnoreCase))
            {
                sensors.AddRange(await ReadBatteryAsync(supplyDir, name, cancellationToken).ConfigureAwait(false));
            }
            else
            {
                sensors.AddRange(await ReadPowerSourceAsync(supplyDir, name, type, cancellationToken).ConfigureAwait(false));
            }
        }

        return sensors;
    }

    private async Task<IEnumerable<SensorReading>> ReadBatteryAsync(string directory, string name, CancellationToken cancellationToken)
    {
        var list = new List<SensorReading>();
        var status = await ReadTrimmedAsync(Path.Combine(directory, "status"), cancellationToken).ConfigureAwait(false);
        var statusGroup = GroupPath.From("Battery", name, "Status");

        if (!string.IsNullOrWhiteSpace(status))
        {
            list.Add(SensorFactory.Create(
                $"battery.{name}.status",
                statusGroup,
                $"{name} Status",
                SensorType.BatteryPercentage,
                null,
                string.Empty,
                Name,
                textValue: status,
                metadata: new Dictionary<string, string> { { "typeDisplay", "Status" } }));
        }

        if (await TryReadDoubleAsync(Path.Combine(directory, "capacity"), cancellationToken).ConfigureAwait(false) is double capacity)
        {
            list.Add(SensorFactory.Create(
                $"battery.{name}.capacity",
                statusGroup,
                $"{name} Charge",
                SensorType.BatteryPercentage,
                capacity,
                "%",
                Name,
                textValue: status));
        }

        var voltage = NormalizeFromMicros(await TryReadDoubleAsync(Path.Combine(directory, "voltage_now"), cancellationToken).ConfigureAwait(false));
        var current = NormalizeFromMicros(await TryReadDoubleAsync(Path.Combine(directory, "current_now"), cancellationToken).ConfigureAwait(false));
        var powerNow = NormalizeFromMicros(await TryReadDoubleAsync(Path.Combine(directory, "power_now"), cancellationToken).ConfigureAwait(false));

        if (voltage is double volts)
        {
            list.Add(SensorFactory.Create(
                $"battery.{name}.voltage",
                GroupPath.From("Battery", name, "Power"),
                $"{name} Voltage",
                SensorType.BatteryVoltage,
                volts,
                "V",
                Name));
        }

        if (current is double amps)
        {
            list.Add(SensorFactory.Create(
                $"battery.{name}.current",
                GroupPath.From("Battery", name, "Power"),
                $"{name} Current",
                SensorType.BatteryCurrent,
                amps,
                "A",
                Name));
        }

        var computedPower = powerNow ?? (voltage.HasValue && current.HasValue ? voltage.Value * current.Value : null);
        if (computedPower is double watts)
        {
            list.Add(SensorFactory.Create(
                $"battery.{name}.power",
                GroupPath.From("Battery", name, "Power"),
                $"{name} Power",
                SensorType.Power,
                watts,
                "W",
                Name));
        }

        if (await TryReadDoubleAsync(Path.Combine(directory, "temp"), cancellationToken).ConfigureAwait(false) is double tempRaw)
        {
            var tempC = tempRaw > 200 ? tempRaw / 10d : (tempRaw > 100 ? tempRaw / 1000d : tempRaw);
            list.Add(SensorFactory.Create(
                $"battery.{name}.temp",
                GroupPath.From("Battery", name, "Temperature"),
                $"{name} Temp",
                SensorType.BatteryTemperature,
                tempC,
                "C",
                Name));
        }

        var energyNow = NormalizeEnergy(await TryReadDoubleAsync(Path.Combine(directory, "energy_now"), cancellationToken).ConfigureAwait(false));
        var energyFull = NormalizeEnergy(await TryReadDoubleAsync(Path.Combine(directory, "energy_full"), cancellationToken).ConfigureAwait(false));
        var energyDesign = NormalizeEnergy(await TryReadDoubleAsync(Path.Combine(directory, "energy_full_design"), cancellationToken).ConfigureAwait(false));

        if (energyNow is double nowWh)
        {
            var description = energyFull.HasValue ? $"Full {energyFull.Value:F1} Wh" : null;
            list.Add(SensorFactory.Create(
                $"battery.{name}.energy",
                GroupPath.From("Battery", name, "Energy"),
                $"{name} Energy",
                SensorType.Energy,
                nowWh,
                "Wh",
                Name,
                description: description));
        }

        if (energyFull.HasValue && energyDesign.HasValue && energyDesign.Value > 0.01)
        {
            var wear = Math.Clamp(100d * (1 - (energyFull.Value / energyDesign.Value)), 0d, 100d);
            list.Add(SensorFactory.Create(
                $"battery.{name}.wear",
                GroupPath.From("Battery", name, "Health"),
                $"{name} Wear",
                SensorType.BatteryPercentage,
                wear,
                "%",
                Name,
                metadata: new Dictionary<string, string> { { "typeDisplay", "Battery Wear" } },
                description: $"Design {energyDesign.Value:F1} Wh"));
        }

        return list;
    }

    private async Task<IEnumerable<SensorReading>> ReadPowerSourceAsync(string directory, string name, string? type, CancellationToken cancellationToken)
    {
        var list = new List<SensorReading>();
        var isUsb = IsUsbSource(name, type);

        if (await TryReadDoubleAsync(Path.Combine(directory, "online"), cancellationToken).ConfigureAwait(false) is double online)
        {
            var statusText = online > 0.5 ? "Online" : "Offline";
            list.Add(SensorFactory.Create(
                $"power.{name}.online",
                GroupPath.From("Power", isUsb ? "USB-C" : "AC Adapter", name, "Status"),
                $"{name} Status",
                SensorType.Power,
                online,
                string.Empty,
                Name,
                textValue: statusText,
                metadata: new Dictionary<string, string> { { "typeDisplay", "Status" } }));
        }

        var voltage = NormalizeFromMicros(await TryReadDoubleAsync(Path.Combine(directory, "voltage_now"), cancellationToken).ConfigureAwait(false));
        var current = NormalizeFromMicros(await TryReadDoubleAsync(Path.Combine(directory, "current_now"), cancellationToken).ConfigureAwait(false));
        var power = NormalizeFromMicros(await TryReadDoubleAsync(Path.Combine(directory, "power_now"), cancellationToken).ConfigureAwait(false));

        if (voltage is double volts)
        {
            list.Add(SensorFactory.Create(
                $"power.{name}.voltage",
                GroupPath.From("Power", isUsb ? "USB-C" : "AC Adapter", name, "Power"),
                $"{name} Voltage",
                SensorType.Voltage,
                volts,
                "V",
                Name));
        }

        if (current is double amps)
        {
            list.Add(SensorFactory.Create(
                $"power.{name}.current",
                GroupPath.From("Power", isUsb ? "USB-C" : "AC Adapter", name, "Power"),
                $"{name} Current",
                SensorType.Current,
                amps,
                "A",
                Name));
        }

        var computed = power ?? (voltage.HasValue && current.HasValue ? voltage.Value * current.Value : null);
        if (computed is double watts)
        {
            list.Add(SensorFactory.Create(
                $"power.{name}.power",
                GroupPath.From("Power", isUsb ? "USB-C" : "AC Adapter", name, "Power"),
                $"{name} Power",
                SensorType.Power,
                watts,
                "W",
                Name,
                description: isUsb ? "USB/Type-C source" : "AC source"));
        }

        return list;
    }

    private static double? NormalizeFromMicros(double? value)
    {
        if (value is null)
        {
            return null;
        }

        var val = value.Value;
        if (val > 1_000_000)
        {
            return val / 1_000_000d;
        }

        if (val > 1000)
        {
            return val / 1000d;
        }

        return val;
    }

    private static double? NormalizeEnergy(double? value)
    {
        if (value is null)
        {
            return null;
        }

        var val = value.Value;
        if (val > 1_000_000)
        {
            return val / 1_000_000d;
        }

        if (val > 1000)
        {
            return val / 1000d;
        }

        return val;
    }

    private static async Task<double?> TryReadDoubleAsync(string path, CancellationToken cancellationToken)
    {
        var text = await ReadTrimmedAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
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

    private static bool IsUsbSource(string name, string? type)
    {
        if (type is not null && type.Contains("usb", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.Contains("usb", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("ucsi", StringComparison.OrdinalIgnoreCase);
    }
}
