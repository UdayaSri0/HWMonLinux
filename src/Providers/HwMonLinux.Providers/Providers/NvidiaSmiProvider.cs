using System.Globalization;
using HwMonLinux.Core;
using HwMonLinux.Providers.Abstractions;

namespace HwMonLinux.Providers.Providers;

public sealed class NvidiaSmiProvider : ISensorProvider
{
    private readonly IProcessRunner _processRunner;

    public NvidiaSmiProvider(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string Name => "nvidia-smi";

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        _processRunner.IsAvailableAsync("nvidia-smi", cancellationToken);

    public async Task<IReadOnlyCollection<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken)
    {
        var args = "--query-gpu=index,name,temperature.gpu,utilization.gpu,fan.speed,memory.used,memory.total,power.draw,power.limit --format=csv,noheader,nounits";
        var result = await _processRunner.RunAsync("nvidia-smi", args, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Array.Empty<SensorReading>();
        }

        var sensors = new List<SensorReading>();
        using var reader = new StringReader(result.StandardOutput);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parts = line.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
            {
                continue;
            }

            var index = parts[0];
            var name = parts[1];

            if (TryParseDouble(parts[2], out var temp))
            {
                sensors.Add(SensorFactory.Create(
                    $"gpu.{index}.temp",
                    GroupPath.From("GPU", $"NVIDIA {name}", "Temperatures"),
                    $"{name} Temp",
                    SensorType.GpuTemperature,
                    temp,
                    "C",
                    Name));
            }

            if (TryParseDouble(parts[3], out var load))
            {
                sensors.Add(SensorFactory.Create(
                    $"gpu.{index}.load",
                    GroupPath.From("GPU", $"NVIDIA {name}", "Utilization"),
                    $"{name} Utilization",
                    SensorType.GpuLoad,
                    load,
                    "%",
                    Name));
            }

            if (TryParseDouble(parts[4], out var fan))
            {
                sensors.Add(SensorFactory.Create(
                    $"gpu.{index}.fan",
                    GroupPath.From("GPU", $"NVIDIA {name}", "Fans"),
                    $"{name} Fan",
                    SensorType.GpuFanSpeed,
                    fan,
                    "%",
                    Name));
            }

            if (parts.Length >= 7 &&
                TryParseDouble(parts[5], out var memUsed) &&
                TryParseDouble(parts[6], out var memTotal) &&
                memTotal > 0.01)
            {
                var percent = Math.Clamp(memUsed / memTotal * 100d, 0d, 100d);
                sensors.Add(SensorFactory.Create(
                    $"gpu.{index}.memory",
                    GroupPath.From("GPU", $"NVIDIA {name}", "Memory"),
                    $"{name} Memory",
                    SensorType.GpuMemoryUsage,
                    percent,
                    "%",
                    Name,
                    description: $"{memUsed:F0}/{memTotal:F0} MiB"));
            }

            if (parts.Length >= 8 && TryParseDouble(parts[7], out var powerDraw))
            {
                sensors.Add(SensorFactory.Create(
                    $"gpu.{index}.power.draw",
                    GroupPath.From("GPU", $"NVIDIA {name}", "Powers"),
                    $"{name} Power Draw",
                    SensorType.Power,
                    powerDraw,
                    "W",
                    Name));
            }

            if (parts.Length >= 9 && TryParseDouble(parts[8], out var powerLimit))
            {
                sensors.Add(SensorFactory.Create(
                    $"gpu.{index}.power.limit",
                    GroupPath.From("GPU", $"NVIDIA {name}", "Powers"),
                    $"{name} Power Limit",
                    SensorType.Power,
                    powerLimit,
                    "W",
                    Name));
            }
        }

        return sensors;
    }

    private static bool TryParseDouble(string value, out double parsed) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
}
