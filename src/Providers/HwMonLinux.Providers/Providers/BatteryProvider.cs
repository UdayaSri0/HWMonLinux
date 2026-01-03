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
        foreach (var batteryDir in Directory.EnumerateDirectories(_powerSupplyRoot, "BAT*"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(batteryDir);
            sensors.AddRange(await ReadBatteryAsync(batteryDir, name, cancellationToken).ConfigureAwait(false));
        }

        return sensors;
    }

    private async Task<IEnumerable<SensorReading>> ReadBatteryAsync(string directory, string name, CancellationToken cancellationToken)
    {
        var list = new List<SensorReading>();
        var group = GroupPath.From("Power", name);

        if (await TryReadDoubleAsync(Path.Combine(directory, "capacity"), cancellationToken) is double capacity)
        {
            var status = await ReadTrimmedAsync(Path.Combine(directory, "status"), cancellationToken).ConfigureAwait(false);
            list.Add(SensorFactory.Create(
                $"battery.{name}.capacity",
                group,
                $"{name} Charge",
                SensorType.BatteryPercentage,
                capacity,
                "%",
                Name,
                textValue: status));
        }

        if (await TryReadDoubleAsync(Path.Combine(directory, "voltage_now"), cancellationToken) is double microVolts)
        {
            var volts = microVolts > 1000 ? microVolts / 1_000_000d : microVolts;
            list.Add(SensorFactory.Create(
                $"battery.{name}.voltage",
                GroupPath.From("Power", name, "Voltage"),
                $"{name} Voltage",
                SensorType.BatteryVoltage,
                volts,
                "V",
                Name));
        }

        if (await TryReadDoubleAsync(Path.Combine(directory, "current_now"), cancellationToken) is double microAmps)
        {
            var amps = microAmps > 1000 ? microAmps / 1_000_000d : microAmps;
            list.Add(SensorFactory.Create(
                $"battery.{name}.current",
                GroupPath.From("Power", name, "Current"),
                $"{name} Current",
                SensorType.BatteryCurrent,
                amps,
                "A",
                Name));
        }

        if (await TryReadDoubleAsync(Path.Combine(directory, "temp"), cancellationToken) is double tempRaw)
        {
            var tempC = tempRaw > 100 ? tempRaw / 10d : tempRaw;
            list.Add(SensorFactory.Create(
                $"battery.{name}.temp",
                GroupPath.From("Power", name, "Temperature"),
                $"{name} Temp",
                SensorType.BatteryTemperature,
                tempC,
                "C",
                Name));
        }

        if (await TryReadDoubleAsync(Path.Combine(directory, "energy_now"), cancellationToken) is double energyNow &&
            await TryReadDoubleAsync(Path.Combine(directory, "energy_full"), cancellationToken) is double energyFull &&
            energyFull > 0)
        {
            list.Add(SensorFactory.Create(
                $"battery.{name}.energy",
                GroupPath.From("Power", name, "Energy"),
                $"{name} Energy",
                SensorType.Energy,
                energyNow,
                "mWh",
                Name,
                description: $"Full {energyFull:F0} mWh"));
        }

        return list;
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
}
