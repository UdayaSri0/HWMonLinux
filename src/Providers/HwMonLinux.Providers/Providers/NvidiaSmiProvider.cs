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
        var args = "--query-gpu=index,name,temperature.gpu,utilization.gpu,fan.speed --format=csv,noheader,nounits";
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
                sensors.Add(new SensorReading($"gpu.{index}.temp", $"{name} Temp", SensorType.GpuTemperature, temp, "C", Name));
            }

            if (TryParseDouble(parts[3], out var load))
            {
                sensors.Add(new SensorReading($"gpu.{index}.load", $"{name} Load", SensorType.GpuLoad, load, "%", Name));
            }

            if (TryParseDouble(parts[4], out var fan))
            {
                sensors.Add(new SensorReading($"gpu.{index}.fan", $"{name} Fan", SensorType.GpuFanSpeed, fan, "%", Name));
            }
        }

        return sensors;
    }

    private static bool TryParseDouble(string value, out double parsed) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
}
